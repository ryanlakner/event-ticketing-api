using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Concurrency;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations.Commands;

/// <summary>
/// Expires pending reservations whose hold has lapsed and releases their seats. Returns the
/// number expired. Safe to run on every instance at once thanks to optimistic concurrency.
/// </summary>
public sealed record ExpireReservationsCommand : ICommand<int>;

internal sealed class ExpireReservationsCommandHandler(
    IApplicationDbContext db,
    TimeProvider clock,
    IOptions<ReservationOptions> options
) : ICommandHandler<ExpireReservationsCommand, int>
{
    public Task<int> HandleAsync(
        ExpireReservationsCommand command,
        CancellationToken cancellationToken
    ) =>
        ConcurrencyRetry.ExecuteAsync(
            db,
            async ct =>
            {
                var now = clock.GetUtcNow();

                var expired = await db
                    .Reservations.Where(r =>
                        r.Status == ReservationStatus.Pending && r.ExpiresAt <= now
                    )
                    .OrderBy(r => r.ExpiresAt)
                    .Take(options.Value.ExpiryBatchSize)
                    .ToListAsync(ct);

                if (expired.Count == 0)
                {
                    return 0;
                }

                var eventIds = expired.Select(r => r.EventId).Distinct().ToList();
                var events = await db
                    .Events.Where(e => eventIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, ct);

                foreach (var reservation in expired)
                {
                    events[reservation.EventId].ExpireReservation(reservation, now);
                }

                await db.SaveChangesAsync(ct);
                return expired.Count;
            },
            cancellationToken
        );
}

using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Concurrency;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Events.Commands;

/// <summary>Cancels an event along with every reservation still holding seats.</summary>
public sealed record CancelEventCommand(Guid Id) : ICommand;

internal sealed class CancelEventCommandHandler(IApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<CancelEventCommand, Unit>
{
    public Task<Unit> HandleAsync(
        CancelEventCommand command,
        CancellationToken cancellationToken
    ) =>
        ConcurrencyRetry.ExecuteAsync(
            db,
            async ct =>
            {
                var @event =
                    await db.Events.FindAsync([command.Id], ct)
                    ?? throw new NotFoundException(nameof(Event), command.Id);

                var activeReservations = await db
                    .Reservations.Where(r =>
                        r.EventId == command.Id
                        && (
                            r.Status == ReservationStatus.Pending
                            || r.Status == ReservationStatus.Confirmed
                        )
                    )
                    .ToListAsync(ct);

                @event.Cancel(activeReservations, clock.GetUtcNow());
                await db.SaveChangesAsync(ct);

                return Unit.Value;
            },
            cancellationToken
        );
}

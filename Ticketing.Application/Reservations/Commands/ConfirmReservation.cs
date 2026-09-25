using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Concurrency;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations.Commands;

/// <summary>
/// Completes checkout for a pending reservation before its hold expires. Only the customer who
/// made it can confirm; to anyone else it does not exist.
/// </summary>
public sealed record ConfirmReservationCommand(Guid Id) : ICommand;

internal sealed class ConfirmReservationCommandHandler(
    IApplicationDbContext db,
    TimeProvider clock,
    ICurrentUser user
) : ICommandHandler<ConfirmReservationCommand, Unit>
{
    public Task<Unit> HandleAsync(
        ConfirmReservationCommand command,
        CancellationToken cancellationToken
    ) =>
        ConcurrencyRetry.ExecuteAsync(
            db,
            async ct =>
            {
                var reservation = await db.Reservations.FindAsync([command.Id], ct);
                if (reservation is null || !reservation.IsHeldBy(user.Id))
                {
                    throw new NotFoundException(nameof(Reservation), command.Id);
                }

                reservation.Confirm(clock.GetUtcNow());
                await db.SaveChangesAsync(ct);

                return Unit.Value;
            },
            cancellationToken
        );
}

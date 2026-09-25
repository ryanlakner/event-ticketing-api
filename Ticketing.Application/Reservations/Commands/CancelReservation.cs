using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Concurrency;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations.Commands;

/// <summary>
/// Cancels a pending or confirmed reservation and returns its seats to the event. Allowed for the
/// customer who made it and for the event's organizer.
/// </summary>
public sealed record CancelReservationCommand(Guid Id) : ICommand;

internal sealed class CancelReservationCommandHandler(
    IApplicationDbContext db,
    TimeProvider clock,
    ICurrentUser user
) : ICommandHandler<CancelReservationCommand, Unit>
{
    public Task<Unit> HandleAsync(
        CancelReservationCommand command,
        CancellationToken cancellationToken
    ) =>
        ConcurrencyRetry.ExecuteAsync(
            db,
            async ct =>
            {
                var reservation =
                    await db.Reservations.FindAsync([command.Id], ct)
                    ?? throw new NotFoundException(nameof(Reservation), command.Id);
                var @event = (await db.Events.FindAsync([reservation.EventId], ct))!;

                if (!reservation.IsHeldBy(user.Id) && !@event.IsOrganizedBy(user.Id))
                {
                    throw new NotFoundException(nameof(Reservation), command.Id);
                }

                @event.CancelReservation(reservation, clock.GetUtcNow());
                await db.SaveChangesAsync(ct);

                return Unit.Value;
            },
            cancellationToken
        );
}

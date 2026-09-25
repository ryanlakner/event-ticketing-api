using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Concurrency;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations.Commands;

/// <summary>Holds seats for a customer until the reservation is confirmed or its hold expires.</summary>
public sealed record ReserveTicketsCommand(Guid EventId, string CustomerEmail, int Quantity)
    : ICommand<Guid>;

internal sealed class ReserveTicketsCommandValidator : AbstractValidator<ReserveTicketsCommand>
{
    public ReserveTicketsCommandValidator()
    {
        RuleFor(c => c.EventId).NotEmpty();
        RuleFor(c => c.CustomerEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(Reservation.EmailMaxLength);
        RuleFor(c => c.Quantity).InclusiveBetween(1, Event.MaxTicketsPerReservation);
    }
}

internal sealed class ReserveTicketsCommandHandler(
    IApplicationDbContext db,
    TimeProvider clock,
    IOptions<ReservationOptions> options
) : ICommandHandler<ReserveTicketsCommand, Guid>
{
    // The event row's concurrency token makes the seat check-and-increment atomic: if two
    // requests read the same seat count, only the first save wins and the other retries.
    public Task<Guid> HandleAsync(
        ReserveTicketsCommand command,
        CancellationToken cancellationToken
    ) =>
        ConcurrencyRetry.ExecuteAsync(
            db,
            async ct =>
            {
                var @event =
                    await db.Events.FindAsync([command.EventId], ct)
                    ?? throw new NotFoundException(nameof(Event), command.EventId);

                var now = clock.GetUtcNow();
                if (@event.SeatsAvailable < command.Quantity)
                {
                    await ReclaimLapsedHoldsAsync(@event, now, ct);
                }

                var reservation = @event.Reserve(
                    command.CustomerEmail,
                    command.Quantity,
                    now,
                    options.Value.HoldDuration
                );

                db.Reservations.Add(reservation);
                await db.SaveChangesAsync(ct);

                return reservation.Id;
            },
            cancellationToken
        );

    // Releases this event's expired holds inline, so correctness never depends on the
    // background sweep having run recently.
    private async Task ReclaimLapsedHoldsAsync(
        Event @event,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var lapsed = await db
            .Reservations.Where(r =>
                r.EventId == @event.Id
                && r.Status == ReservationStatus.Pending
                && r.ExpiresAt <= now
            )
            .ToListAsync(cancellationToken);

        foreach (var reservation in lapsed)
        {
            @event.ExpireReservation(reservation, now);
        }
    }
}

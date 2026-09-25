using System.Linq.Expressions;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations;

public sealed record ReservationDto(
    Guid Id,
    Guid EventId,
    string EventName,
    DateTimeOffset EventStartsAt,
    string CustomerId,
    string CustomerEmail,
    int Quantity,
    ReservationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset CreatedAt
)
{
    /// <summary>Projection translated to SQL so queries only select the columns they need.</summary>
    internal static readonly Expression<Func<ReservationWithEvent, ReservationDto>> Projection =
        x => new ReservationDto(
            x.Reservation.Id,
            x.Reservation.EventId,
            x.Event.Name,
            x.Event.StartsAt,
            x.Reservation.CustomerId,
            x.Reservation.CustomerEmail,
            x.Reservation.Quantity,
            x.Reservation.Status,
            x.Reservation.ExpiresAt,
            x.Reservation.ConfirmedAt,
            x.Reservation.CreatedAt
        );
}

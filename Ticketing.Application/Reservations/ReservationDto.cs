using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations;

public sealed record ReservationDto(
    Guid Id,
    Guid EventId,
    string EventName,
    DateTimeOffset EventStartsAt,
    string CustomerEmail,
    int Quantity,
    ReservationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset CreatedAt
);

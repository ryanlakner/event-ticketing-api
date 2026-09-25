using Ticketing.Domain.Common;

namespace Ticketing.Domain.Reservations;

/// <summary>
/// Seats held for a customer. Created by <see cref="Events.Event.Reserve"/>; changes that release
/// seats go through the owning event so the seat count stays consistent.
/// </summary>
public sealed class Reservation : Entity
{
    public const int EmailMaxLength = 320;

    // Required by EF Core.
    private Reservation() { }

    public Guid EventId { get; private set; }

    /// <summary>The user (Entra ID object ID) who made the reservation.</summary>
    public string CustomerId { get; private set; } = string.Empty;

    public string CustomerEmail { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public ReservationStatus Status { get; private set; }

    /// <summary>When an unconfirmed hold lapses and its seats return to the pool.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public bool IsHeldBy(string? userId) => userId is not null && userId == CustomerId;

    /// <summary>Whether the reservation is currently holding seats.</summary>
    public bool IsActive => Status is ReservationStatus.Pending or ReservationStatus.Confirmed;

    internal static Reservation Create(
        Guid eventId,
        string customerId,
        string customerEmail,
        int quantity,
        DateTimeOffset now,
        DateTimeOffset expiresAt
    )
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("A reservation must belong to a customer.");
        }

        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            throw new DomainException("Customer email is required.");
        }

        return new Reservation
        {
            EventId = eventId,
            CustomerId = customerId,
            CustomerEmail = customerEmail.Trim().ToLowerInvariant(),
            Quantity = quantity,
            Status = ReservationStatus.Pending,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
    }

    public void Confirm(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Pending)
        {
            throw new DomainException(
                $"Only pending reservations can be confirmed (status: {Status})."
            );
        }

        if (now >= ExpiresAt)
        {
            throw new DomainException("The reservation hold has expired.");
        }

        Status = ReservationStatus.Confirmed;
        ConfirmedAt = now;
        MarkModified(now);
    }

    internal void Cancel(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new DomainException(
                $"The reservation is already {Status.ToString().ToLowerInvariant()}."
            );
        }

        Status = ReservationStatus.Cancelled;
        MarkModified(now);
    }

    internal void Expire(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Pending || now < ExpiresAt)
        {
            throw new DomainException("Only pending reservations past their hold can expire.");
        }

        Status = ReservationStatus.Expired;
        MarkModified(now);
    }
}

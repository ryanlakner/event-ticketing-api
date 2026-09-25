using Ticketing.Domain.Common;
using Ticketing.Domain.Reservations;

namespace Ticketing.Domain.Events;

/// <summary>
/// A ticketed event. Owns the seat count, so every reservation that holds or releases seats goes
/// through this aggregate and is protected by its concurrency token.
/// </summary>
public sealed class Event : Entity
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int VenueMaxLength = 200;
    public const int MaxCapacity = 100_000;
    public const int MaxTicketsPerReservation = 10;
    public const int UserIdMaxLength = 128;

    // Required by EF Core.
    private Event() { }

    /// <summary>The user (Entra ID object ID) who created and manages the event.</summary>
    public string OrganizerId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string Venue { get; private set; } = string.Empty;

    public DateTimeOffset StartsAt { get; private set; }

    public int Capacity { get; private set; }

    /// <summary>Seats held by pending or confirmed reservations.</summary>
    public int SeatsReserved { get; private set; }

    public EventStatus Status { get; private set; }

    public int SeatsAvailable => Capacity - SeatsReserved;

    public static Event Create(
        string organizerId,
        string name,
        string description,
        string venue,
        DateTimeOffset startsAt,
        int capacity,
        DateTimeOffset now
    )
    {
        if (string.IsNullOrWhiteSpace(organizerId))
        {
            throw new DomainException("An event must have an organizer.");
        }

        var @event = new Event
        {
            OrganizerId = organizerId,
            CreatedAt = now,
            Status = EventStatus.Draft,
        };
        @event.SetDetails(name, description, venue, startsAt, capacity, now);
        return @event;
    }

    public void UpdateDetails(
        string name,
        string description,
        string venue,
        DateTimeOffset startsAt,
        int capacity,
        DateTimeOffset now
    )
    {
        EnsureNotCancelled();

        if (capacity < SeatsReserved)
        {
            throw new DomainException(
                $"Capacity cannot be reduced below the {SeatsReserved} seats already reserved."
            );
        }

        SetDetails(name, description, venue, startsAt, capacity, now);
        MarkModified(now);
    }

    public void Publish(DateTimeOffset now)
    {
        if (Status != EventStatus.Draft)
        {
            throw new DomainException($"Only draft events can be published (status: {Status}).");
        }

        EnsureNotStarted(now);
        Status = EventStatus.Published;
        MarkModified(now);
    }

    /// <summary>Cancels the event and every reservation that still holds seats.</summary>
    public void Cancel(IEnumerable<Reservation> activeReservations, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(activeReservations);
        EnsureNotCancelled();

        foreach (var reservation in activeReservations)
        {
            EnsureOwns(reservation);
            reservation.Cancel(now);
        }

        Status = EventStatus.Cancelled;
        SeatsReserved = 0;
        MarkModified(now);
    }

    /// <summary>Holds seats for a customer until the reservation is confirmed or expires.</summary>
    public bool IsOrganizedBy(string? userId) => userId is not null && userId == OrganizerId;

    public Reservation Reserve(
        string customerId,
        string customerEmail,
        int quantity,
        DateTimeOffset now,
        TimeSpan holdDuration
    )
    {
        if (Status != EventStatus.Published)
        {
            throw new DomainException("Tickets can only be reserved for published events.");
        }

        EnsureNotStarted(now);

        if (quantity is < 1 or > MaxTicketsPerReservation)
        {
            throw new DomainException(
                $"A reservation must be for 1-{MaxTicketsPerReservation} tickets."
            );
        }

        if (quantity > SeatsAvailable)
        {
            throw new DomainException(
                SeatsAvailable == 0
                    ? "This event is sold out."
                    : $"Only {SeatsAvailable} seats are available."
            );
        }

        SeatsReserved += quantity;
        MarkModified(now);

        return Reservation.Create(Id, customerId, customerEmail, quantity, now, now + holdDuration);
    }

    public void CancelReservation(Reservation reservation, DateTimeOffset now)
    {
        EnsureOwns(reservation);
        reservation.Cancel(now);
        ReleaseSeats(reservation.Quantity, now);
    }

    public void ExpireReservation(Reservation reservation, DateTimeOffset now)
    {
        EnsureOwns(reservation);
        reservation.Expire(now);
        ReleaseSeats(reservation.Quantity, now);
    }

    private void ReleaseSeats(int quantity, DateTimeOffset now)
    {
        SeatsReserved = Math.Max(0, SeatsReserved - quantity);
        MarkModified(now);
    }

    private void SetDetails(
        string name,
        string description,
        string venue,
        DateTimeOffset startsAt,
        int capacity,
        DateTimeOffset now
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Event name is required.");
        }

        if (string.IsNullOrWhiteSpace(venue))
        {
            throw new DomainException("Venue is required.");
        }

        if (startsAt <= now)
        {
            throw new DomainException("Events must start in the future.");
        }

        if (capacity is < 1 or > MaxCapacity)
        {
            throw new DomainException($"Capacity must be between 1 and {MaxCapacity}.");
        }

        Name = name.Trim();
        Description = description.Trim();
        Venue = venue.Trim();
        StartsAt = startsAt.ToUniversalTime();
        Capacity = capacity;
    }

    private void EnsureNotCancelled()
    {
        if (Status == EventStatus.Cancelled)
        {
            throw new DomainException("The event has been cancelled.");
        }
    }

    private void EnsureNotStarted(DateTimeOffset now)
    {
        if (now >= StartsAt)
        {
            throw new DomainException("The event has already started.");
        }
    }

    private void EnsureOwns(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        if (reservation.EventId != Id)
        {
            throw new InvalidOperationException("Reservation belongs to a different event.");
        }
    }
}

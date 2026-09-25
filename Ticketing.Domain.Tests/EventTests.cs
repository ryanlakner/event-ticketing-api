using Ticketing.Domain.Common;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Domain.Tests;

public sealed class EventTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hold = TimeSpan.FromMinutes(10);

    private static Event PublishedEvent(int capacity = 10)
    {
        var @event = Event.Create("Jazz Night", "", "Grand Hall", Now.AddDays(7), capacity, Now);
        @event.Publish(Now);
        return @event;
    }

    [Fact]
    public void Create_starts_as_draft_with_all_seats_available_and_utc_start()
    {
        var startsAt = new DateTimeOffset(2026, 6, 1, 19, 30, 0, TimeSpan.FromHours(-4));

        var @event = Event.Create(" Jazz Night ", "", "Grand Hall", startsAt, 100, Now);

        @event.Status.ShouldBe(EventStatus.Draft);
        @event.Name.ShouldBe("Jazz Night");
        @event.SeatsAvailable.ShouldBe(100);
        @event.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        @event.StartsAt.ShouldBe(startsAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(Event.MaxCapacity + 1)]
    public void Create_rejects_out_of_range_capacity(int capacity) =>
        Should.Throw<DomainException>(() =>
            Event.Create("Show", "", "Hall", Now.AddDays(1), capacity, Now)
        );

    [Fact]
    public void Create_rejects_start_in_the_past() =>
        Should.Throw<DomainException>(() =>
            Event.Create("Show", "", "Hall", Now.AddMinutes(-1), 10, Now)
        );

    [Fact]
    public void Reserve_holds_seats_until_expiry()
    {
        var @event = PublishedEvent(capacity: 10);

        var reservation = @event.Reserve("Fan@Example.com", 3, Now, Hold);

        @event.SeatsAvailable.ShouldBe(7);
        reservation.Status.ShouldBe(ReservationStatus.Pending);
        reservation.CustomerEmail.ShouldBe("fan@example.com");
        reservation.ExpiresAt.ShouldBe(Now + Hold);
    }

    [Fact]
    public void Reserve_rejects_draft_events()
    {
        var draft = Event.Create("Show", "", "Hall", Now.AddDays(1), 10, Now);

        Should
            .Throw<DomainException>(() => draft.Reserve("a@b.com", 1, Now, Hold))
            .Message.ShouldContain("published");
    }

    [Fact]
    public void Reserve_rejects_more_seats_than_available()
    {
        var @event = PublishedEvent(capacity: 3);
        @event.Reserve("a@b.com", 2, Now, Hold);

        Should
            .Throw<DomainException>(() => @event.Reserve("c@d.com", 2, Now, Hold))
            .Message.ShouldBe("Only 1 seats are available.");
        @event.SeatsAvailable.ShouldBe(1);
    }

    [Fact]
    public void Reserve_reports_sold_out()
    {
        var @event = PublishedEvent(capacity: 1);
        @event.Reserve("a@b.com", 1, Now, Hold);

        Should
            .Throw<DomainException>(() => @event.Reserve("c@d.com", 1, Now, Hold))
            .Message.ShouldBe("This event is sold out.");
    }

    [Fact]
    public void Reserve_rejects_events_that_have_started()
    {
        var @event = PublishedEvent();

        Should.Throw<DomainException>(() => @event.Reserve("a@b.com", 1, Now.AddDays(8), Hold));
    }

    [Fact]
    public void CancelReservation_releases_seats()
    {
        var @event = PublishedEvent(capacity: 5);
        var reservation = @event.Reserve("a@b.com", 4, Now, Hold);

        @event.CancelReservation(reservation, Now);

        reservation.Status.ShouldBe(ReservationStatus.Cancelled);
        @event.SeatsAvailable.ShouldBe(5);
    }

    [Fact]
    public void ExpireReservation_releases_seats_only_after_the_hold_lapses()
    {
        var @event = PublishedEvent(capacity: 5);
        var reservation = @event.Reserve("a@b.com", 2, Now, Hold);

        Should.Throw<DomainException>(() =>
            @event.ExpireReservation(reservation, Now.AddMinutes(9))
        );

        @event.ExpireReservation(reservation, Now + Hold);
        reservation.Status.ShouldBe(ReservationStatus.Expired);
        @event.SeatsAvailable.ShouldBe(5);
    }

    [Fact]
    public void Confirmed_reservations_never_expire()
    {
        var @event = PublishedEvent();
        var reservation = @event.Reserve("a@b.com", 1, Now, Hold);
        reservation.Confirm(Now.AddMinutes(1));

        Should.Throw<DomainException>(() => @event.ExpireReservation(reservation, Now.AddHours(1)));
        reservation.Status.ShouldBe(ReservationStatus.Confirmed);
    }

    [Fact]
    public void UpdateDetails_cannot_drop_capacity_below_reserved_seats()
    {
        var @event = PublishedEvent(capacity: 10);
        @event.Reserve("a@b.com", 6, Now, Hold);

        Should.Throw<DomainException>(() =>
            @event.UpdateDetails("Show", "", "Hall", @event.StartsAt, 5, Now)
        );
        @event.Capacity.ShouldBe(10);
    }

    [Fact]
    public void Cancel_cancels_every_active_reservation()
    {
        var @event = PublishedEvent();
        var pending = @event.Reserve("a@b.com", 1, Now, Hold);
        var confirmed = @event.Reserve("c@d.com", 2, Now, Hold);
        confirmed.Confirm(Now);

        @event.Cancel([pending, confirmed], Now);

        @event.Status.ShouldBe(EventStatus.Cancelled);
        pending.Status.ShouldBe(ReservationStatus.Cancelled);
        confirmed.Status.ShouldBe(ReservationStatus.Cancelled);
        Should.Throw<DomainException>(() => @event.Reserve("e@f.com", 1, Now, Hold));
    }

    [Fact]
    public void Every_state_change_issues_a_new_concurrency_version()
    {
        var @event = Event.Create("Show", "", "Hall", Now.AddDays(1), 10, Now);
        var versions = new HashSet<Guid> { @event.Version };

        @event.Publish(Now);
        versions.Add(@event.Version).ShouldBeTrue();

        var reservation = @event.Reserve("a@b.com", 1, Now, Hold);
        versions.Add(@event.Version).ShouldBeTrue();

        @event.CancelReservation(reservation, Now);
        versions.Add(@event.Version).ShouldBeTrue();
    }
}

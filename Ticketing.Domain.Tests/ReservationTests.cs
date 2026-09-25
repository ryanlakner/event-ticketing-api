using Ticketing.Domain.Common;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Domain.Tests;

public sealed class ReservationTests
{
    private const string Organizer = "organizer-1";
    private const string Customer = "customer-1";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hold = TimeSpan.FromMinutes(10);

    private static Reservation PendingReservation()
    {
        var @event = Event.Create(Organizer, "Show", "", "Hall", Now.AddDays(1), 10, Now);
        @event.Publish(Now);
        return @event.Reserve(Customer, "a@b.com", 2, Now, Hold);
    }

    [Fact]
    public void Confirm_within_hold_marks_confirmed()
    {
        var reservation = PendingReservation();

        reservation.Confirm(Now.AddMinutes(5));

        reservation.Status.ShouldBe(ReservationStatus.Confirmed);
        reservation.ConfirmedAt.ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public void Confirm_after_hold_expires_is_rejected()
    {
        var reservation = PendingReservation();

        Should
            .Throw<DomainException>(() => reservation.Confirm(Now + Hold))
            .Message.ShouldContain("expired");
        reservation.Status.ShouldBe(ReservationStatus.Pending);
    }

    [Fact]
    public void Confirm_twice_is_rejected()
    {
        var reservation = PendingReservation();
        reservation.Confirm(Now);

        Should.Throw<DomainException>(() => reservation.Confirm(Now));
    }
}

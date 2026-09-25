using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Application.Events.Commands;
using Ticketing.Application.Events.Queries;
using Ticketing.Application.Reservations.Commands;
using Ticketing.Application.Reservations.Queries;
using Ticketing.Domain.Common;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Tests.Reservations;

public sealed class ReservationHandlerTests
{
    private static readonly TimeSpan PastHold = TimeSpan.FromMinutes(11);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReserveTickets_holds_seats_for_the_calling_customer()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 10, name: "Jazz");

        var id = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "fan@example.com", 3),
            Users.Customer
        );

        var reservation = await harness.QueryAsync(new GetReservationByIdQuery(id), Users.Customer);
        reservation.Status.ShouldBe(ReservationStatus.Pending);
        reservation.CustomerId.ShouldBe(Users.Customer);
        reservation.EventName.ShouldBe("Jazz");
        reservation.ExpiresAt.ShouldBe(harness.Clock.GetUtcNow().AddMinutes(10));
        (await harness.QueryAsync(new GetEventByIdQuery(eventId), null)).SeatsAvailable.ShouldBe(7);
    }

    [Fact]
    public async Task ReserveTickets_validates_email_and_quantity()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            harness.SendAsync(
                new ReserveTicketsCommand(eventId, "not-an-email", 11),
                Users.Customer
            )
        );

        ex.Errors.Select(e => e.PropertyName)
            .ShouldBe(["CustomerEmail", "Quantity"], ignoreOrder: true);
    }

    [Fact]
    public async Task ReserveTickets_cannot_find_draft_events()
    {
        await using var harness = new TestHarness();
        var draft = await harness.SendAsync(
            new CreateEventCommand("Draft", "", "Hall", harness.Clock.GetUtcNow().AddDays(1), 5),
            Users.Organizer
        );

        await Should.ThrowAsync<NotFoundException>(() =>
            harness.SendAsync(new ReserveTicketsCommand(draft, "a@b.com", 1), Users.Customer)
        );
    }

    [Fact]
    public async Task ReserveTickets_rejects_when_sold_out()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 2);
        await harness.SendAsync(new ReserveTicketsCommand(eventId, "a@b.com", 2), Users.Customer);

        await Should.ThrowAsync<DomainException>(() =>
            harness.SendAsync(new ReserveTicketsCommand(eventId, "c@d.com", 1), Users.OtherCustomer)
        );
    }

    [Fact]
    public async Task ReserveTickets_reclaims_lapsed_holds_without_waiting_for_the_sweep()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 2);
        var lapsed = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "a@b.com", 2),
            Users.Customer
        );
        harness.Clock.Advance(PastHold);

        await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "c@d.com", 2),
            Users.OtherCustomer
        );

        (
            await harness.QueryAsync(new GetReservationByIdQuery(lapsed), Users.Customer)
        ).Status.ShouldBe(ReservationStatus.Expired);
        (await harness.QueryAsync(new GetEventByIdQuery(eventId), null)).SeatsAvailable.ShouldBe(0);
    }

    [Fact]
    public async Task Reservations_are_visible_only_to_their_customer_and_the_events_organizer()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(organizer: Users.Organizer);
        var id = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "a@b.com", 1),
            Users.Customer
        );

        (await harness.QueryAsync(new GetReservationByIdQuery(id), Users.Customer)).Id.ShouldBe(id);
        (await harness.QueryAsync(new GetReservationByIdQuery(id), Users.Organizer)).Id.ShouldBe(
            id
        );
        foreach (var outsider in new[] { null, Users.OtherCustomer, Users.OtherOrganizer })
        {
            await Should.ThrowAsync<NotFoundException>(() =>
                harness.QueryAsync(new GetReservationByIdQuery(id), outsider)
            );
        }
    }

    [Fact]
    public async Task Only_the_customer_can_confirm_their_reservation()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync();
        var id = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "a@b.com", 1),
            Users.Customer
        );

        await Should.ThrowAsync<NotFoundException>(() =>
            harness.SendAsync(new ConfirmReservationCommand(id), Users.OtherCustomer)
        );
        await harness.SendAsync(new ConfirmReservationCommand(id), Users.Customer);
    }

    [Fact]
    public async Task ConfirmReservation_after_hold_expires_is_rejected()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync();
        var id = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "a@b.com", 1),
            Users.Customer
        );
        harness.Clock.Advance(PastHold);

        await Should.ThrowAsync<DomainException>(() =>
            harness.SendAsync(new ConfirmReservationCommand(id), Users.Customer)
        );
    }

    [Theory]
    [InlineData(Users.Customer)]
    [InlineData(Users.Organizer)]
    public async Task CancelReservation_by_customer_or_organizer_returns_seats(string canceller)
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 4);
        var id = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "a@b.com", 4),
            Users.Customer
        );

        await harness.SendAsync(new CancelReservationCommand(id), canceller);

        (await harness.QueryAsync(new GetEventByIdQuery(eventId), null)).SeatsAvailable.ShouldBe(4);
    }

    [Fact]
    public async Task CancelReservation_is_not_found_for_unrelated_users()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(organizer: Users.Organizer);
        var id = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "a@b.com", 1),
            Users.Customer
        );

        await Should.ThrowAsync<NotFoundException>(() =>
            harness.SendAsync(new CancelReservationCommand(id), Users.OtherOrganizer)
        );
    }

    [Fact]
    public async Task ExpireReservations_releases_only_lapsed_pending_holds()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 10);
        var lapsed = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "a@b.com", 1),
            Users.Customer
        );
        var confirmed = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "c@d.com", 2),
            Users.Customer
        );
        await harness.SendAsync(new ConfirmReservationCommand(confirmed), Users.Customer);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        var fresh = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "e@f.com", 3),
            Users.Customer
        );
        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        // The background sweep runs with no signed-in user.
        var expiredCount = await harness.SendAsync(new ExpireReservationsCommand(), user: null);

        expiredCount.ShouldBe(1);
        (
            await harness.QueryAsync(new GetReservationByIdQuery(lapsed), Users.Customer)
        ).Status.ShouldBe(ReservationStatus.Expired);
        (
            await harness.QueryAsync(new GetReservationByIdQuery(fresh), Users.Customer)
        ).Status.ShouldBe(ReservationStatus.Pending);
        (await harness.QueryAsync(new GetEventByIdQuery(eventId), null)).SeatsAvailable.ShouldBe(5);
    }

    [Fact]
    public async Task Stale_writes_are_rejected_by_the_concurrency_token()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 1);

        // Two "requests" read the same version of the event; only the first save may win.
        await harness.DbAsync(async first =>
        {
            var stale = await first.Events.SingleAsync(e => e.Id == eventId, Ct);
            await harness.SendAsync(
                new ReserveTicketsCommand(eventId, "winner@b.com", 1),
                Users.Customer
            );

            first.Reservations.Add(
                stale.Reserve(
                    Users.OtherCustomer,
                    "loser@b.com",
                    1,
                    harness.Clock.GetUtcNow(),
                    TimeSpan.FromMinutes(10)
                )
            );
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync(Ct));
            return 0;
        });

        (await harness.DbAsync(db => db.Reservations.CountAsync(Ct))).ShouldBe(1);
    }
}

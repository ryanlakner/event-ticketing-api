using FluentValidation;
using Microsoft.EntityFrameworkCore;
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
    public async Task ReserveTickets_holds_seats_and_returns_a_pending_reservation()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 10, name: "Jazz");

        var id = await harness.SendAsync(new ReserveTicketsCommand(eventId, "fan@example.com", 3));

        var reservation = await harness.QueryAsync(new GetReservationByIdQuery(id));
        reservation.Status.ShouldBe(ReservationStatus.Pending);
        reservation.EventName.ShouldBe("Jazz");
        reservation.ExpiresAt.ShouldBe(harness.Clock.GetUtcNow().AddMinutes(10));
        (await harness.QueryAsync(new GetEventByIdQuery(eventId))).SeatsAvailable.ShouldBe(7);
    }

    [Fact]
    public async Task ReserveTickets_validates_email_and_quantity()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            harness.SendAsync(new ReserveTicketsCommand(eventId, "not-an-email", 11))
        );

        ex.Errors.Select(e => e.PropertyName)
            .ShouldBe(["CustomerEmail", "Quantity"], ignoreOrder: true);
    }

    [Fact]
    public async Task ReserveTickets_rejects_when_sold_out()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 2);
        await harness.SendAsync(new ReserveTicketsCommand(eventId, "a@b.com", 2));

        await Should.ThrowAsync<DomainException>(() =>
            harness.SendAsync(new ReserveTicketsCommand(eventId, "c@d.com", 1))
        );
    }

    [Fact]
    public async Task ReserveTickets_reclaims_lapsed_holds_without_waiting_for_the_sweep()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 2);
        var lapsed = await harness.SendAsync(new ReserveTicketsCommand(eventId, "a@b.com", 2));
        harness.Clock.Advance(PastHold);

        await harness.SendAsync(new ReserveTicketsCommand(eventId, "c@d.com", 2));

        (await harness.QueryAsync(new GetReservationByIdQuery(lapsed))).Status.ShouldBe(
            ReservationStatus.Expired
        );
        (await harness.QueryAsync(new GetEventByIdQuery(eventId))).SeatsAvailable.ShouldBe(0);
    }

    [Fact]
    public async Task ConfirmReservation_after_hold_expires_is_rejected()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync();
        var id = await harness.SendAsync(new ReserveTicketsCommand(eventId, "a@b.com", 1));
        harness.Clock.Advance(PastHold);

        await Should.ThrowAsync<DomainException>(() =>
            harness.SendAsync(new ConfirmReservationCommand(id))
        );
    }

    [Fact]
    public async Task CancelReservation_returns_seats_to_the_event()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 4);
        var id = await harness.SendAsync(new ReserveTicketsCommand(eventId, "a@b.com", 4));

        await harness.SendAsync(new CancelReservationCommand(id));

        (await harness.QueryAsync(new GetEventByIdQuery(eventId))).SeatsAvailable.ShouldBe(4);
    }

    [Fact]
    public async Task ExpireReservations_releases_only_lapsed_pending_holds()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync(capacity: 10);
        var lapsed = await harness.SendAsync(new ReserveTicketsCommand(eventId, "a@b.com", 1));
        var confirmed = await harness.SendAsync(new ReserveTicketsCommand(eventId, "c@d.com", 2));
        await harness.SendAsync(new ConfirmReservationCommand(confirmed));
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        var fresh = await harness.SendAsync(new ReserveTicketsCommand(eventId, "e@f.com", 3));
        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        var expiredCount = await harness.SendAsync(new ExpireReservationsCommand());

        expiredCount.ShouldBe(1);
        (await harness.QueryAsync(new GetReservationByIdQuery(lapsed))).Status.ShouldBe(
            ReservationStatus.Expired
        );
        (await harness.QueryAsync(new GetReservationByIdQuery(fresh))).Status.ShouldBe(
            ReservationStatus.Pending
        );
        (await harness.QueryAsync(new GetEventByIdQuery(eventId))).SeatsAvailable.ShouldBe(5);
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
            await harness.SendAsync(new ReserveTicketsCommand(eventId, "winner@b.com", 1));

            first.Reservations.Add(
                stale.Reserve("loser@b.com", 1, harness.Clock.GetUtcNow(), TimeSpan.FromMinutes(10))
            );
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync(Ct));
            return 0;
        });

        (await harness.DbAsync(db => db.Reservations.CountAsync(Ct))).ShouldBe(1);
    }
}

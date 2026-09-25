using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Application.Events.Commands;
using Ticketing.Application.Events.Queries;
using Ticketing.Application.Reservations.Commands;
using Ticketing.Domain.Common;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Tests.Events;

public sealed class EventHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateEvent_persists_a_draft()
    {
        await using var harness = new TestHarness();

        var id = await harness.SendAsync(
            new CreateEventCommand("Jazz", "Live", "Hall", harness.Clock.GetUtcNow().AddDays(1), 50)
        );

        var dto = await harness.QueryAsync(new GetEventByIdQuery(id));
        dto.Status.ShouldBe(EventStatus.Draft);
        dto.SeatsAvailable.ShouldBe(50);
    }

    [Fact]
    public async Task CreateEvent_validates_input_before_reaching_the_handler()
    {
        await using var harness = new TestHarness();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            harness.SendAsync(
                new CreateEventCommand("", "", "Hall", harness.Clock.GetUtcNow().AddDays(-1), 0)
            )
        );

        ex.Errors.Select(e => e.PropertyName)
            .ShouldBe(["Name", "StartsAt", "Capacity"], ignoreOrder: true);
        (await harness.DbAsync(db => db.Events.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task PublishEvent_twice_is_a_domain_conflict()
    {
        await using var harness = new TestHarness();
        var id = await harness.CreatePublishedEventAsync();

        await Should.ThrowAsync<DomainException>(() =>
            harness.SendAsync(new PublishEventCommand(id))
        );
    }

    [Fact]
    public async Task UpdateEvent_throws_not_found_for_unknown_id()
    {
        await using var harness = new TestHarness();

        await Should.ThrowAsync<NotFoundException>(() =>
            harness.SendAsync(
                new UpdateEventCommand(
                    Guid.NewGuid(),
                    "Name",
                    "",
                    "Hall",
                    harness.Clock.GetUtcNow().AddDays(1),
                    10
                )
            )
        );
    }

    [Fact]
    public async Task CancelEvent_cancels_all_active_reservations()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync();
        await harness.SendAsync(new ReserveTicketsCommand(eventId, "a@b.com", 1));
        var confirmed = await harness.SendAsync(new ReserveTicketsCommand(eventId, "c@d.com", 2));
        await harness.SendAsync(new ConfirmReservationCommand(confirmed));

        await harness.SendAsync(new CancelEventCommand(eventId));

        var statuses = await harness.DbAsync(db =>
            db.Reservations.Select(r => r.Status).Distinct().ToListAsync(Ct)
        );
        statuses.ShouldBe([ReservationStatus.Cancelled]);
        (await harness.QueryAsync(new GetEventByIdQuery(eventId))).Status.ShouldBe(
            EventStatus.Cancelled
        );
    }

    [Fact]
    public async Task ListEvents_filters_by_status_and_orders_by_start_time()
    {
        await using var harness = new TestHarness();
        var now = harness.Clock.GetUtcNow();
        await harness.SendAsync(new CreateEventCommand("Draft", "", "Hall", now.AddDays(1), 10));
        foreach (var (name, days) in new[] { ("Later", 9), ("Sooner", 2) })
        {
            var id = await harness.SendAsync(
                new CreateEventCommand(name, "", "Hall", now.AddDays(days), 10)
            );
            await harness.SendAsync(new PublishEventCommand(id));
        }

        var page = await harness.QueryAsync(new ListEventsQuery(Status: EventStatus.Published));

        page.TotalCount.ShouldBe(2);
        page.Items.Select(e => e.Name).ShouldBe(["Sooner", "Later"]);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    public async Task ListEvents_rejects_invalid_paging(int page, int pageSize)
    {
        await using var harness = new TestHarness();

        await Should.ThrowAsync<ValidationException>(() =>
            harness.QueryAsync(new ListEventsQuery(page, pageSize))
        );
    }
}

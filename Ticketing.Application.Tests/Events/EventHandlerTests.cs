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
    public async Task CreateEvent_persists_a_draft_owned_by_the_caller()
    {
        await using var harness = new TestHarness();

        var id = await harness.SendAsync(
            new CreateEventCommand(
                "Jazz",
                "Live",
                "Hall",
                harness.Clock.GetUtcNow().AddDays(1),
                50
            ),
            Users.Organizer
        );

        var dto = await harness.QueryAsync(new GetEventByIdQuery(id), Users.Organizer);
        dto.Status.ShouldBe(EventStatus.Draft);
        dto.SeatsAvailable.ShouldBe(50);
        var organizerId = await harness.DbAsync(db =>
            db.Events.Where(e => e.Id == id).Select(e => e.OrganizerId).SingleAsync(Ct)
        );
        organizerId.ShouldBe(Users.Organizer);
    }

    [Fact]
    public async Task CreateEvent_validates_input_before_reaching_the_handler()
    {
        await using var harness = new TestHarness();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            harness.SendAsync(
                new CreateEventCommand("", "", "Hall", harness.Clock.GetUtcNow().AddDays(-1), 0),
                Users.Organizer
            )
        );

        ex.Errors.Select(e => e.PropertyName)
            .ShouldBe(["Name", "StartsAt", "Capacity"], ignoreOrder: true);
        (await harness.DbAsync(db => db.Events.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task CreateEvent_fails_closed_for_anonymous_callers()
    {
        await using var harness = new TestHarness();

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            harness.SendAsync(
                new CreateEventCommand("Jazz", "", "Hall", harness.Clock.GetUtcNow().AddDays(1), 5),
                user: null
            )
        );
    }

    [Fact]
    public async Task PublishEvent_twice_is_a_domain_conflict()
    {
        await using var harness = new TestHarness();
        var id = await harness.CreatePublishedEventAsync();

        await Should.ThrowAsync<DomainException>(() =>
            harness.SendAsync(new PublishEventCommand(id), Users.Organizer)
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
                ),
                Users.Organizer
            )
        );
    }

    [Fact]
    public async Task Organizers_cannot_manage_another_organizers_published_event()
    {
        await using var harness = new TestHarness();
        var id = await harness.CreatePublishedEventAsync(organizer: Users.Organizer);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            harness.SendAsync(new CancelEventCommand(id), Users.OtherOrganizer)
        );
        (await harness.QueryAsync(new GetEventByIdQuery(id), null)).Status.ShouldBe(
            EventStatus.Published
        );
    }

    [Fact]
    public async Task Drafts_are_invisible_to_everyone_but_their_organizer()
    {
        await using var harness = new TestHarness();
        var draft = await harness.SendAsync(
            new CreateEventCommand("Secret", "", "Hall", harness.Clock.GetUtcNow().AddDays(1), 5),
            Users.Organizer
        );

        foreach (var outsider in new[] { null, Users.Customer, Users.OtherOrganizer })
        {
            await Should.ThrowAsync<NotFoundException>(() =>
                harness.QueryAsync(new GetEventByIdQuery(draft), outsider)
            );
            (await harness.QueryAsync(new ListEventsQuery(), outsider)).TotalCount.ShouldBe(0);
        }

        // Managing someone else's draft looks the same as it not existing.
        await Should.ThrowAsync<NotFoundException>(() =>
            harness.SendAsync(new PublishEventCommand(draft), Users.OtherOrganizer)
        );
        (await harness.QueryAsync(new ListEventsQuery(), Users.Organizer)).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task CancelEvent_cancels_all_active_reservations()
    {
        await using var harness = new TestHarness();
        var eventId = await harness.CreatePublishedEventAsync();
        await harness.SendAsync(new ReserveTicketsCommand(eventId, "a@b.com", 1), Users.Customer);
        var confirmed = await harness.SendAsync(
            new ReserveTicketsCommand(eventId, "c@d.com", 2),
            Users.OtherCustomer
        );
        await harness.SendAsync(new ConfirmReservationCommand(confirmed), Users.OtherCustomer);

        await harness.SendAsync(new CancelEventCommand(eventId), Users.Organizer);

        var statuses = await harness.DbAsync(db =>
            db.Reservations.Select(r => r.Status).Distinct().ToListAsync(Ct)
        );
        statuses.ShouldBe([ReservationStatus.Cancelled]);
        (await harness.QueryAsync(new GetEventByIdQuery(eventId), null)).Status.ShouldBe(
            EventStatus.Cancelled
        );
    }

    [Fact]
    public async Task ListEvents_filters_by_status_and_orders_by_start_time()
    {
        await using var harness = new TestHarness();
        var now = harness.Clock.GetUtcNow();
        await harness.SendAsync(
            new CreateEventCommand("Draft", "", "Hall", now.AddDays(1), 10),
            Users.Organizer
        );
        foreach (var (name, days) in new[] { ("Later", 9), ("Sooner", 2) })
        {
            var id = await harness.SendAsync(
                new CreateEventCommand(name, "", "Hall", now.AddDays(days), 10),
                Users.Organizer
            );
            await harness.SendAsync(new PublishEventCommand(id), Users.Organizer);
        }

        var page = await harness.QueryAsync(
            new ListEventsQuery(Status: EventStatus.Published),
            Users.Organizer
        );

        page.TotalCount.ShouldBe(2);
        page.Items.Select(e => e.Name).ShouldBe(["Sooner", "Later"]);
    }

    [Fact]
    public async Task ListMyEvents_returns_only_the_callers_events_including_drafts()
    {
        await using var harness = new TestHarness();
        var now = harness.Clock.GetUtcNow();
        var draft = await harness.SendAsync(
            new CreateEventCommand("My draft", "", "Hall", now.AddDays(5), 10),
            Users.Organizer
        );
        var published = await harness.CreatePublishedEventAsync(name: "My show");
        await harness.CreatePublishedEventAsync(
            name: "Their show",
            organizer: Users.OtherOrganizer
        );

        var mine = await harness.QueryAsync(new ListMyEventsQuery(), Users.Organizer);

        mine.TotalCount.ShouldBe(2);
        mine.Items.Select(e => e.Id).ShouldBe([draft, published], ignoreOrder: true);
    }

    [Fact]
    public async Task ListMyEvents_filters_by_status()
    {
        await using var harness = new TestHarness();
        await harness.SendAsync(
            new CreateEventCommand("Draft", "", "Hall", harness.Clock.GetUtcNow().AddDays(1), 10),
            Users.Organizer
        );
        var published = await harness.CreatePublishedEventAsync();

        var page = await harness.QueryAsync(
            new ListMyEventsQuery(Status: EventStatus.Published),
            Users.Organizer
        );

        page.Items.Select(e => e.Id).ShouldBe([published]);
    }

    [Fact]
    public async Task ListMyEvents_requires_a_signed_in_user()
    {
        await using var harness = new TestHarness();

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            harness.QueryAsync(new ListMyEventsQuery(), user: null)
        );
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    public async Task ListEvents_rejects_invalid_paging(int page, int pageSize)
    {
        await using var harness = new TestHarness();

        await Should.ThrowAsync<ValidationException>(() =>
            harness.QueryAsync(new ListEventsQuery(page, pageSize), null)
        );
    }
}

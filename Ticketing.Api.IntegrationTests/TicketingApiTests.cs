using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ticketing.Api.Contracts;
using Ticketing.Application.Common.Security;
using Ticketing.Application.Events;
using Ticketing.Application.Reservations;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Api.IntegrationTests;

public sealed class TicketingApiTests(TicketingApiFactory factory)
    : IClassFixture<TicketingApiFactory>
{
    private readonly HttpClient _anonymous = factory.CreateClient();
    private readonly HttpClient _organizer = factory.CreateClientAs(
        Users.Organizer,
        Roles.Organizer
    );
    private readonly HttpClient _customer = factory.CreateClientAs(Users.Customer, Roles.Customer);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => factory.Clock.GetUtcNow();

    [Fact]
    public async Task Customer_can_reserve_and_confirm_tickets()
    {
        var @event = await _organizer.CreatePublishedEventAsync(Now, 100);

        var reserved = await _customer.PostAsJsonAsync(
            $"/api/events/{@event.Id}/reservations",
            new ReserveTicketsRequest("fan@example.com", 2),
            Ct
        );
        reserved.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reservation = await reserved.ReadAsync<ReservationDto>();
        reserved.Headers.Location!.AbsolutePath.ShouldBe($"/api/reservations/{reservation.Id}");
        reservation.Status.ShouldBe(ReservationStatus.Pending);
        reservation.CustomerId.ShouldBe(Users.Customer);

        (
            await _customer.PostAsync($"/api/reservations/{reservation.Id}/confirm", null, Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var confirmed = await (
            await _customer.GetAsync($"/api/reservations/{reservation.Id}", Ct)
        ).ReadAsync<ReservationDto>();
        confirmed.Status.ShouldBe(ReservationStatus.Confirmed);

        var eventJson = await _anonymous.GetStringAsync($"/api/events/{@event.Id}", Ct);
        eventJson.ShouldContain("\"status\":\"Published\"");
        eventJson.ShouldContain("\"seatsAvailable\":98");
    }

    [Fact]
    public async Task Concurrent_reservations_never_oversell_an_event()
    {
        const int capacity = 5;
        var @event = await _organizer.CreatePublishedEventAsync(Now, capacity);

        // Far more buyers than seats, all at once, each with their own identity.
        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 25)
                .Select(i =>
                    factory
                        .CreateClientAs($"buyer-{i}", Roles.Customer)
                        .PostAsJsonAsync(
                            $"/api/events/{@event.Id}/reservations",
                            new ReserveTicketsRequest($"buyer{i}@example.com", 1),
                            Ct
                        )
                )
        );

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        responses
            .Where(r => r.StatusCode != HttpStatusCode.Created)
            .ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict);
        created.ShouldBeInRange(1, capacity);

        // The seat counter and the reservations table must agree exactly.
        var (seatsReserved, reservationCount) = await factory.DbAsync(async db =>
        {
            var seats = await db
                .Events.Where(e => e.Id == @event.Id)
                .Select(e => e.SeatsReserved)
                .SingleAsync(Ct);
            var count = await db.Reservations.CountAsync(r => r.EventId == @event.Id, Ct);
            return (seats, count);
        });
        seatsReserved.ShouldBe(created);
        reservationCount.ShouldBe(created);
    }

    [Fact]
    public async Task Expired_hold_cannot_be_confirmed()
    {
        var @event = await _organizer.CreatePublishedEventAsync(Now, 10);
        var reservation = await (
            await _customer.PostAsJsonAsync(
                $"/api/events/{@event.Id}/reservations",
                new ReserveTicketsRequest("late@example.com", 1),
                Ct
            )
        ).ReadAsync<ReservationDto>();

        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        var response = await _customer.PostAsync(
            $"/api/reservations/{reservation.Id}/confirm",
            null,
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadAsync<ProblemDetails>()).Detail.ShouldBe(
            "The reservation hold has expired."
        );
    }

    [Fact]
    public async Task Anonymous_callers_can_browse_but_not_change_anything()
    {
        var @event = await _organizer.CreatePublishedEventAsync(Now, 10);

        (await _anonymous.GetAsync("/api/events", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _anonymous.GetAsync($"/api/events/{@event.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.OK
        );

        var create = await _anonymous.PostAsJsonAsync(
            "/api/events",
            new EventRequest("Show", null, "Hall", Now.AddDays(1), 10),
            Ct
        );
        var reserve = await _anonymous.PostAsJsonAsync(
            $"/api/events/{@event.Id}/reservations",
            new ReserveTicketsRequest("a@b.com", 1),
            Ct
        );
        create.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        reserve.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Roles_gate_what_each_user_can_do()
    {
        var @event = await _organizer.CreatePublishedEventAsync(Now, 10);

        var customerCreates = await _customer.PostAsJsonAsync(
            "/api/events",
            new EventRequest("Show", null, "Hall", Now.AddDays(1), 10),
            Ct
        );
        var organizerReserves = await _organizer.PostAsJsonAsync(
            $"/api/events/{@event.Id}/reservations",
            new ReserveTicketsRequest("a@b.com", 1),
            Ct
        );

        customerCreates.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        organizerReserves.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Organizers_can_only_manage_their_own_events()
    {
        var @event = await _organizer.CreatePublishedEventAsync(Now, 10);
        var otherOrganizer = factory.CreateClientAs(Users.OtherOrganizer, Roles.Organizer);

        var response = await otherOrganizer.PostAsync($"/api/events/{@event.Id}/cancel", null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadAsync<ProblemDetails>()).Detail.ShouldBe(
            "Only the event's organizer can manage it."
        );
    }

    [Fact]
    public async Task Drafts_are_hidden_from_everyone_but_their_organizer()
    {
        var draft = await _organizer.CreateDraftEventAsync(Now, 10);

        (await _organizer.GetAsync($"/api/events/{draft.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.OK
        );
        (await _anonymous.GetAsync($"/api/events/{draft.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
        (await _customer.GetAsync($"/api/events/{draft.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task Reservations_are_private_to_the_customer_and_organizer()
    {
        var @event = await _organizer.CreatePublishedEventAsync(Now, 10);
        var reservation = await (
            await _customer.PostAsJsonAsync(
                $"/api/events/{@event.Id}/reservations",
                new ReserveTicketsRequest("fan@example.com", 1),
                Ct
            )
        ).ReadAsync<ReservationDto>();
        var otherCustomer = factory.CreateClientAs(Users.OtherCustomer, Roles.Customer);

        (await _organizer.GetAsync($"/api/reservations/{reservation.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.OK
        );
        (
            await otherCustomer.GetAsync($"/api/reservations/{reservation.Id}", Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (
            await otherCustomer.PostAsync($"/api/reservations/{reservation.Id}/confirm", null, Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tokens_for_another_audience_are_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            factory.CreateToken(
                Users.Organizer,
                [Roles.Organizer],
                audience: "api://some-other-app"
            )
        );

        var response = await client.PostAsJsonAsync(
            "/api/events",
            new EventRequest("Show", null, "Hall", Now.AddDays(1), 10),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reserving_a_draft_event_returns_not_found()
    {
        var draft = await _organizer.CreateDraftEventAsync(Now, 10);

        var response = await _customer.PostAsJsonAsync(
            $"/api/events/{draft.Id}/reservations",
            new ReserveTicketsRequest("a@b.com", 1),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Invalid_event_returns_validation_problem()
    {
        var response = await _organizer.PostAsJsonAsync(
            "/api/events",
            new EventRequest("", null, "", Now.AddDays(-1), 0),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        problem.Errors.Keys.ShouldBe(["Name", "Venue", "StartsAt", "Capacity"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_event_returns_not_found()
    {
        var response = await _anonymous.GetAsync($"/api/events/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Events_can_be_filtered_by_status()
    {
        await _organizer.CreatePublishedEventAsync(Now, 10);

        var json = await _organizer.GetStringAsync(
            $"/api/events?status={EventStatus.Published}",
            Ct
        );

        json.ShouldContain("\"status\":\"Published\"");
        json.ShouldNotContain("\"status\":\"Draft\"");
    }

    [Fact]
    public async Task Health_endpoint_is_public() =>
        (await _anonymous.GetStringAsync("/health", Ct)).ShouldBe("Healthy");

    [Fact]
    public async Task Swagger_document_describes_the_api_and_its_security()
    {
        var json = await _anonymous.GetStringAsync("/swagger/v1/swagger.json", Ct);

        json.ShouldContain("/api/events/{id}/reservations");
        json.ShouldContain("\"securitySchemes\"");
        json.ShouldContain("Requires role: **Organizer**");
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ticketing.Api.Contracts;
using Ticketing.Application.Events;
using Ticketing.Application.Reservations;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Api.IntegrationTests;

public sealed class TicketingApiTests(TicketingApiFactory factory)
    : IClassFixture<TicketingApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Customer_can_reserve_and_confirm_tickets()
    {
        var @event = await _client.CreatePublishedEventAsync(factory.Clock.GetUtcNow(), 100);

        var reserved = await _client.PostAsJsonAsync(
            $"/api/events/{@event.Id}/reservations",
            new ReserveTicketsRequest("fan@example.com", 2),
            Ct
        );
        reserved.StatusCode.ShouldBe(HttpStatusCode.Created);
        var reservation = await reserved.ReadAsync<ReservationDto>();
        reserved.Headers.Location!.AbsolutePath.ShouldBe($"/api/reservations/{reservation.Id}");
        reservation.Status.ShouldBe(ReservationStatus.Pending);

        (
            await _client.PostAsync($"/api/reservations/{reservation.Id}/confirm", null, Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var confirmed = await (
            await _client.GetAsync($"/api/reservations/{reservation.Id}", Ct)
        ).ReadAsync<ReservationDto>();
        confirmed.Status.ShouldBe(ReservationStatus.Confirmed);

        var eventJson = await _client.GetStringAsync($"/api/events/{@event.Id}", Ct);
        eventJson.ShouldContain("\"status\":\"Published\"");
        eventJson.ShouldContain("\"seatsAvailable\":98");
    }

    [Fact]
    public async Task Concurrent_reservations_never_oversell_an_event()
    {
        const int capacity = 5;
        var @event = await _client.CreatePublishedEventAsync(factory.Clock.GetUtcNow(), capacity);

        // Far more buyers than seats, all at once.
        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 25)
                .Select(i =>
                    _client.PostAsJsonAsync(
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
        var @event = await _client.CreatePublishedEventAsync(factory.Clock.GetUtcNow(), 10);
        var reservation = await (
            await _client.PostAsJsonAsync(
                $"/api/events/{@event.Id}/reservations",
                new ReserveTicketsRequest("late@example.com", 1),
                Ct
            )
        ).ReadAsync<ReservationDto>();

        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        var response = await _client.PostAsync(
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
    public async Task Reserving_a_draft_event_returns_conflict_problem()
    {
        var draft = await (
            await _client.PostAsJsonAsync(
                "/api/events",
                new EventRequest("Draft", null, "Hall", factory.Clock.GetUtcNow().AddDays(1), 10),
                Ct
            )
        ).ReadAsync<EventDto>();

        var response = await _client.PostAsJsonAsync(
            $"/api/events/{draft.Id}/reservations",
            new ReserveTicketsRequest("a@b.com", 1),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Invalid_event_returns_validation_problem()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/events",
            new EventRequest("", null, "", factory.Clock.GetUtcNow().AddDays(-1), 0),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        problem.Errors.Keys.ShouldBe(["Name", "Venue", "StartsAt", "Capacity"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unknown_event_returns_not_found()
    {
        var response = await _client.GetAsync($"/api/events/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Events_can_be_filtered_by_status()
    {
        await _client.CreatePublishedEventAsync(factory.Clock.GetUtcNow(), 10);

        var json = await _client.GetStringAsync($"/api/events?status={EventStatus.Published}", Ct);

        json.ShouldContain("\"status\":\"Published\"");
        json.ShouldNotContain("\"status\":\"Draft\"");
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy() =>
        (await _client.GetStringAsync("/health", Ct)).ShouldBe("Healthy");

    [Fact]
    public async Task Swagger_document_describes_the_api()
    {
        var json = await _client.GetStringAsync("/swagger/v1/swagger.json", Ct);

        json.ShouldContain("/api/events/{id}/reservations");
        json.ShouldContain("/api/reservations/{id}/confirm");
    }
}

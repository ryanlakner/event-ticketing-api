using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ticketing.Api.Contracts;
using Ticketing.Application.Events;

namespace Ticketing.Api.IntegrationTests;

/// <summary>Stable user IDs, as Entra ID would put them in the "oid" claim.</summary>
internal static class Users
{
    public const string Organizer = "11111111-1111-1111-1111-111111111111";
    public const string OtherOrganizer = "22222222-2222-2222-2222-222222222222";
    public const string Customer = "33333333-3333-3333-3333-333333333333";
    public const string OtherCustomer = "44444444-4444-4444-4444-444444444444";
}

internal static class ApiClient
{
    /// <summary>Matches the API's serializer settings (camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.Current.CancellationToken))!;

    public static async Task<EventDto> CreateDraftEventAsync(
        this HttpClient organizer,
        DateTimeOffset now,
        int capacity
    )
    {
        var created = await organizer.PostAsJsonAsync(
            "/api/events",
            new EventRequest("Jazz Night", "Live jazz", "Grand Hall", now.AddDays(30), capacity),
            TestContext.Current.CancellationToken
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.ReadAsync<EventDto>();
    }

    public static async Task<EventDto> CreatePublishedEventAsync(
        this HttpClient organizer,
        DateTimeOffset now,
        int capacity
    )
    {
        var @event = await organizer.CreateDraftEventAsync(now, capacity);
        (
            await organizer.PostAsync(
                $"/api/events/{@event.Id}/publish",
                null,
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return @event;
    }
}

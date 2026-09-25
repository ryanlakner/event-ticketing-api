using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ticketing.Api.Contracts;
using Ticketing.Application.Events;

namespace Ticketing.Api.IntegrationTests;

internal static class ApiClient
{
    /// <summary>Matches the API's serializer settings (camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json, TestContext.Current.CancellationToken))!;

    public static async Task<EventDto> CreatePublishedEventAsync(
        this HttpClient client,
        DateTimeOffset now,
        int capacity
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await client.PostAsJsonAsync(
            "/api/events",
            new EventRequest("Jazz Night", "Live jazz", "Grand Hall", now.AddDays(30), capacity),
            ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var @event = await created.ReadAsync<EventDto>();

        (await client.PostAsync($"/api/events/{@event.Id}/publish", null, ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        return @event;
    }
}

using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace Ticketing.Api.IntegrationTests;

public sealed class CorsTests(TicketingApiFactory factory) : IClassFixture<TicketingApiFactory>
{
    private const string WebOrigin = "https://tickets.example.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient CreateClient() =>
        factory
            .WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins:0", WebOrigin))
            .CreateClient();

    [Fact]
    public async Task Preflight_from_an_allowed_origin_succeeds_without_a_token()
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/me/reservations");
        preflight.Headers.Add("Origin", WebOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await CreateClient().SendAsync(preflight, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([WebOrigin]);
        response
            .Headers.GetValues("Access-Control-Allow-Headers")
            .Single()
            .ShouldContain("authorization");
    }

    [Fact]
    public async Task Responses_to_an_allowed_origin_expose_the_location_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("Origin", WebOrigin);

        var response = await CreateClient().SendAsync(request, Ct);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([WebOrigin]);
        response.Headers.GetValues("Access-Control-Expose-Headers").ShouldBe(["Location"]);
    }

    [Fact]
    public async Task Other_origins_get_no_cors_headers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        request.Headers.Add("Origin", "https://evil.example.test");

        var response = await CreateClient().SendAsync(request, Ct);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}

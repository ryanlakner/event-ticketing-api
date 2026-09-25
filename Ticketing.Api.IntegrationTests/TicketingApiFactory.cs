using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Ticketing.Infrastructure.Persistence;
using Ticketing.Testing;

namespace Ticketing.Api.IntegrationTests;

/// <summary>
/// Hosts the real API in memory against a throwaway SQLite database and a controllable clock.
/// Access tokens are real signed JWTs shaped like Entra ID's ("oid" and "roles" claims), validated
/// by the production JWT bearer setup through the same configuration keys Azure uses.
/// </summary>
public sealed class TicketingApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://login.example.test/tenant/v2.0";
    public const string Audience = "api://ticketing-tests";

    private readonly SqliteTestDatabase _database = new();
    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);

    public FakeTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public HttpClient CreateClientAs(string userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken(userId, roles)
        );
        return client;
    }

    public string CreateToken(string userId, string[] roles, string audience = Audience)
    {
        // No nbf/iat and a far-off expiry, so the token is valid whatever the fake clock says.
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return handler.CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = audience,
                Expires = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Claims = new Dictionary<string, object> { ["oid"] = userId, ["roles"] = roles },
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(_signingKey),
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
    }

    public async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Database", "overridden-in-tests");
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting("Reservations:ExpirySweepEnabled", "false");

        const string bearer = "Authentication:Schemes:Bearer";
        builder.UseSetting($"{bearer}:ValidIssuer", Issuer);
        builder.UseSetting($"{bearer}:ValidAudiences:0", Audience);
        builder.UseSetting($"{bearer}:SigningKeys:0:Issuer", Issuer);
        builder.UseSetting($"{bearer}:SigningKeys:0:Value", Convert.ToBase64String(_signingKey));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(_database.Configure);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        _database.EnsureCreated(host.Services);
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _database.Dispose();
        }
    }
}

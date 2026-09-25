using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Events.Commands;
using Ticketing.Infrastructure.Persistence;
using Ticketing.Testing;

namespace Ticketing.Application.Tests;

/// <summary>User IDs as the API would resolve them from access tokens.</summary>
internal static class Users
{
    public const string Organizer = "organizer-1";
    public const string OtherOrganizer = "organizer-2";
    public const string Customer = "customer-1";
    public const string OtherCustomer = "customer-2";
}

/// <summary>
/// Wires the real Application layer and EF model to a throwaway SQLite database. Each call runs
/// in its own DI scope (a fresh DbContext) as the given user, mirroring one HTTP request.
/// </summary>
internal sealed class TestHarness : IAsyncDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly ServiceProvider _provider;

    public TestHarness()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddDbContext<AppDbContext>(_database.Configure);
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<TestCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>());
        services.AddApplication();

        _provider = services.BuildServiceProvider(validateScopes: true);
        _database.EnsureCreated(_provider);
    }

    public FakeTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <param name="command">The command to dispatch.</param>
    /// <param name="user">Who sends it; <c>null</c> for an anonymous caller.</param>
    public Task<T> SendAsync<T>(ICommand<T> command, string? user) =>
        InScopeAsync(user, sp => sp.GetRequiredService<IDispatcher>().SendAsync(command, Ct));

    /// <param name="query">The query to dispatch.</param>
    /// <param name="user">Who sends it; <c>null</c> for an anonymous caller.</param>
    public Task<T> QueryAsync<T>(IQuery<T> query, string? user) =>
        InScopeAsync(user, sp => sp.GetRequiredService<IDispatcher>().QueryAsync(query, Ct));

    public Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action) =>
        InScopeAsync(null, sp => action(sp.GetRequiredService<AppDbContext>()));

    public async Task<Guid> CreatePublishedEventAsync(
        int capacity = 10,
        string name = "Jazz Night",
        string organizer = Users.Organizer
    )
    {
        var id = await SendAsync(
            new CreateEventCommand(name, "", "Grand Hall", Clock.GetUtcNow().AddDays(7), capacity),
            organizer
        );
        await SendAsync(new PublishEventCommand(id), organizer);
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        _database.Dispose();
    }

    private async Task<T> InScopeAsync<T>(string? user, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TestCurrentUser>().Id = user;
        return await action(scope.ServiceProvider);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public string? Id { get; set; }
    }
}

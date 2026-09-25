using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Events.Commands;
using Ticketing.Infrastructure.Persistence;
using Ticketing.Testing;

namespace Ticketing.Application.Tests;

/// <summary>
/// Wires the real Application layer and EF model to a throwaway SQLite database. Each call runs
/// in its own DI scope (a fresh DbContext), mirroring one HTTP request per operation.
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
        services.AddApplication();

        _provider = services.BuildServiceProvider(validateScopes: true);
        _database.EnsureCreated(_provider);
    }

    public FakeTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Task<T> SendAsync<T>(ICommand<T> command) =>
        InScopeAsync(sp => sp.GetRequiredService<IDispatcher>().SendAsync(command, Ct));

    public Task<T> QueryAsync<T>(IQuery<T> query) =>
        InScopeAsync(sp => sp.GetRequiredService<IDispatcher>().QueryAsync(query, Ct));

    public Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action) =>
        InScopeAsync(sp => action(sp.GetRequiredService<AppDbContext>()));

    public async Task<Guid> CreatePublishedEventAsync(int capacity = 10, string name = "Jazz Night")
    {
        var id = await SendAsync(
            new CreateEventCommand(name, "", "Grand Hall", Clock.GetUtcNow().AddDays(7), capacity)
        );
        await SendAsync(new PublishEventCommand(id));
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        _database.Dispose();
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }
}

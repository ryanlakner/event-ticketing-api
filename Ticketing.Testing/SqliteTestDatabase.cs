using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Ticketing.Infrastructure.Persistence;

namespace Ticketing.Testing;

/// <summary>
/// A throwaway file-backed SQLite database with the production EF model. File-backed (not
/// in-memory) so each DbContext gets its own connection, which lets tests run genuinely
/// concurrent requests and exercise the optimistic concurrency tokens and check constraints.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"ticketing-tests-{Guid.NewGuid():N}.db"
    );

    public void Configure(DbContextOptionsBuilder options) =>
        options
            .UseSqlite($"Data Source={_path};Pooling=False")
            .ReplaceService<IModelCustomizer, SqliteModelCustomizer>();

    public void EnsureCreated(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // WAL lets readers proceed while a writer holds the lock, closer to SQL Server behaviour.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }

    /// <summary>
    /// SQLite cannot compare or order <see cref="DateTimeOffset"/> columns, so store them as
    /// sortable integers in tests only. The production SQL Server model is untouched.
    /// </summary>
    private sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies)
        : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);

            var properties = modelBuilder
                .Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p =>
                    p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)
                );

            foreach (var property in properties)
            {
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            }
        }
    }
}

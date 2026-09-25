using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Data;

namespace Ticketing.Application.Common.Concurrency;

/// <summary>
/// Re-runs a read-modify-write operation when another request saved the same row first.
/// Each attempt re-reads current state, so business rules (e.g. "enough seats left") are
/// re-evaluated against fresh data rather than the stale copy that lost the race.
/// </summary>
internal static class ConcurrencyRetry
{
    public const int MaxAttempts = 5;

    public static async Task<T> ExecuteAsync<T>(
        IApplicationDbContext db,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();

                // Jittered backoff so competing requests don't retry in lockstep.
                await Task.Delay(Random.Shared.Next(5, 20 * attempt), cancellationToken);
            }
        }
    }
}

using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Reservations;
using Ticketing.Application.Reservations.Commands;

namespace Ticketing.Api.BackgroundJobs;

/// <summary>
/// Periodically releases seats held by lapsed reservations. Running on every App Service
/// instance is safe: concurrent sweeps conflict on the concurrency token and retry.
/// </summary>
internal sealed partial class ReservationExpiryService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationOptions> options,
    ILogger<ReservationExpiryService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ExpirySweepEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.ExpirySweepInterval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

                var expired = await dispatcher.SendAsync(
                    new ExpireReservationsCommand(),
                    stoppingToken
                );
                if (expired > 0)
                {
                    LogExpired(expired);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed sweep is retried on the next tick; never crash the host.
                LogSweepFailed(ex);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expired {Count} reservations")]
    private partial void LogExpired(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reservation expiry sweep failed")]
    private partial void LogSweepFailed(Exception exception);
}

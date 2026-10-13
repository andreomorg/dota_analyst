using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.Baseline;

// Background service next to the collector: every Interval, turns the raw base into the finished one and publishes it
// (local database and the Neon mirror). An error only goes to the log and waits for the next round.
public class BaselinePublisherService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<BaselinePublisherService> logger) : BackgroundService
{
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var wait = FirstDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, timeProvider, stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PublishBaselineHandler>().PublishAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (StorageUnavailableException ex)
            {
                logger.LogWarning(ex, "Could not publish the comparison base; trying again in {Interval}", Interval);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Publishing the comparison base failed");
            }

            wait = Interval;
        }
    }
}

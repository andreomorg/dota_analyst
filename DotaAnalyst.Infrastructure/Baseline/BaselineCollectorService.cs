using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.Baseline;

// Background service that keeps adding matches to the comparison base, one at a time (the replay parser does one
// parse at a time anyway). Waits longer when there is nothing new, the daily limit was reached or a source is down;
// an error only goes to the log, it never stops the app.
public class BaselineCollectorService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<BaselineCollectorService> logger) : BackgroundService
{
    // A pause between two matches, so the collector never hogs the parser the site also uses.
    public static readonly TimeSpan BetweenMatches = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan WhenNothingNew = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan WhenLimitReached = TimeSpan.FromHours(1);
    public static readonly TimeSpan WhenUnavailable = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Comparison base collector started");
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                // A scope per step: the handler and its repositories are Scoped.
                await using var scope = scopeFactory.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<CollectorStorageBudget>().CanCollectAsync(stoppingToken))
                {
                    logger.LogWarning("Local collector storage budget reached; collection is paused and existing data is preserved");
                    await Task.Delay(WhenLimitReached, timeProvider, stoppingToken);
                    continue;
                }
                var result = await scope.ServiceProvider.GetRequiredService<CollectBaselineHandler>().CollectNextAsync(stoppingToken);
                wait = result switch
                {
                    CollectResult.Progressed => BetweenMatches,
                    CollectResult.NothingNew => WhenNothingNew,
                    CollectResult.LimitReached => WhenLimitReached,
                    _ => WhenUnavailable,
                };
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (StorageUnavailableException ex)
            {
                logger.LogWarning(ex, "The comparison base can't reach the database");
                wait = WhenUnavailable;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The comparison base collector failed a step");
                wait = WhenUnavailable;
            }

            try
            {
                await Task.Delay(wait, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

using DotaAnalyst.Application.Replays;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// Empties ReplayParseQueue in the background, one replay at a time (the parser itself only takes one at a time,
// and the machine running it is the project owner's). For each replay it runs the ParseReplayHandler use case,
// which parses and saves the result.
// - One DI scope per replay: the handler and the repository are Scoped, and a BackgroundService lives as long
//   as the app, so it creates (and disposes) a scope like a request would.
// - Never stops the app: an error is logged and the loop goes on with the next replay (an exception escaping a
//   BackgroundService would stop the whole host).
// - Parser unavailable -> opens ReplayParserCircuitBreaker; while it is open, what is still waiting is dropped
//   without calling the parser (the next visit queues it again).
public sealed class ReplayParseWorker(
    ReplayParseQueue queue,
    ReplayParserCircuitBreaker circuitBreaker,
    IServiceScopeFactory scopeFactory,
    ILogger<ReplayParseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ParseAsync(request, stoppingToken);
                }
                finally
                {
                    // Saved, failed or dropped: from now on a visit reads the result or queues it again.
                    queue.Complete(request.MatchId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The app is stopping.
        }
    }

    private async Task ParseAsync(ReplayParseRequest request, CancellationToken stoppingToken)
    {
        if (circuitBreaker.IsOpen)
        {
            logger.LogDebug("The replay parser is unavailable; match {MatchId} was dropped from the queue", request.MatchId);
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ParseReplayHandler>();
            var result = await handler.HandleAsync(request.MatchId, request.ReplayUrl, stoppingToken);
            if (result == ParseReplayResult.ParserUnavailable)
            {
                circuitBreaker.Open();
                logger.LogWarning(
                    "Replay parsing paused for {Pause}: the parser is unavailable", ReplayParserCircuitBreaker.OpenFor);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while parsing the replay of match {MatchId}", request.MatchId);
        }
    }
}

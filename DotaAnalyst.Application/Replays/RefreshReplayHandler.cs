using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Replays;

public enum RefreshReplayResult
{
    // Parsed again (or its kept document already had everything); the analysis was saved.
    Refreshed,
    // Neither the saved match nor OpenDota has a replay URL for it.
    NoReplay,
    // OpenDota could not be asked for the replay URL (down or over its quota): nothing changed; try again later.
    SourceUnavailable,
    // Valve no longer has the replay: nothing changed (the analysis and the old document stay).
    Expired,
    // The replay could not be downloaded or read this time: nothing changed.
    Failed,
    // The parser was busy or unavailable: nothing changed; try again later.
    ParserBusy,
    ParserUnavailable,
    // PrefetchAsync: the replay is in the parser's cache (or the kept document is already fresh): a refresh can follow.
    Prefetched,
}

// Use case of the local re-parse (internal route, the owner's machine): parse a match's replay again so its kept
// document gets the newest sections the prediction models read. Unlike ParseReplayHandler, a failure saves nothing: an
// analysis that works today is never replaced by "expired" or "failed" because its replay is gone.
public sealed class RefreshReplayHandler(
    IReplayParser replayParser,
    IReplayAnalysisRepository analyses,
    IMatchRepository matches,
    IReplayAnalyzer openDota,
    TimeProvider timeProvider,
    ILogger<RefreshReplayHandler> logger)
{
    public Task<RefreshReplayResult> HandleAsync(MatchId matchId, CancellationToken cancellationToken) =>
        WithReplayUrlAsync(matchId, async replayUrl =>
        {
            var analysis = await replayParser.ParseFreshAsync(matchId, replayUrl, cancellationToken);
            await analyses.SaveAsync(SavedReplayAnalysis.Ready(analysis, timeProvider.GetUtcNow()), cancellationToken);
            return RefreshReplayResult.Refreshed;
        }, cancellationToken);

    // Downloads the replay into the parser's cache ahead of HandleAsync (a batch keeps the parse slots busy parsing,
    // not waiting on Valve). Saves nothing.
    public Task<RefreshReplayResult> PrefetchAsync(MatchId matchId, CancellationToken cancellationToken) =>
        WithReplayUrlAsync(matchId, async replayUrl =>
        {
            await replayParser.PrefetchAsync(matchId, replayUrl, cancellationToken);
            return RefreshReplayResult.Prefetched;
        }, cancellationToken);

    private async Task<RefreshReplayResult> WithReplayUrlAsync(
        MatchId matchId, Func<Uri, Task<RefreshReplayResult>> use, CancellationToken cancellationToken)
    {
        // The site's saved matches have the replay URL; the comparison base's (most of the kept documents) don't, so
        // OpenDota is asked, as the collector does.
        var replayUrl = (await matches.FindAsync(matchId, cancellationToken))?.Match.ReplayUrl;
        if (replayUrl is null)
        {
            try
            {
                replayUrl = (await openDota.GetMatchDetailsAsync(matchId, cancellationToken))?.ReplayUrl;
            }
            catch (DataSourceUnavailableException)
            {
                return RefreshReplayResult.SourceUnavailable;
            }
        }

        if (replayUrl is null)
        {
            return RefreshReplayResult.NoReplay;
        }

        try
        {
            return await use(replayUrl);
        }
        catch (ReplayExpiredException)
        {
            return RefreshReplayResult.Expired;
        }
        catch (ReplayUnreadableException ex)
        {
            logger.LogWarning(ex, "Could not parse the replay of match {MatchId} again; the saved analysis stays", matchId);
            return RefreshReplayResult.Failed;
        }
        catch (ReplayParserBusyException)
        {
            return RefreshReplayResult.ParserBusy;
        }
        catch (ReplayParserUnavailableException)
        {
            return RefreshReplayResult.ParserUnavailable;
        }
    }
}

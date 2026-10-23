using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Matches;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Replays;

public enum ParseReplayResult
{
    // Parsed; saved as ready.
    Ready,
    // The replay could not be downloaded or read; saved as failed (tried again later).
    Failed,
    // Valve doesn't have the replay; saved as expired (see SavedReplayState.Expired).
    Expired,
    // The parser stayed busy with other replays; nothing saved. The match can be queued again on the next visit.
    ParserBusy,
    // The parser could not be used; nothing saved. Parsing should pause for a while (the worker's circuit breaker).
    ParserUnavailable,
}

// Use case of the background worker: parses one match's replay and saves the result, so the next visit to the
// analysis shows the laning phase. A database failure is only logged: the result is lost and the replay is
// parsed again on a later visit.
public class ParseReplayHandler(
    IReplayParser replayParser,
    IReplayAnalysisRepository repository,
    TimeProvider timeProvider,
    ILogger<ParseReplayHandler> logger)
{
    public async Task<ParseReplayResult> HandleAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
    {
        SavedReplayAnalysis result;
        try
        {
            var analysis = await replayParser.ParseAsync(matchId, replayUrl, cancellationToken);
            result = SavedReplayAnalysis.Ready(analysis, timeProvider.GetUtcNow());
        }
        catch (ReplayExpiredException)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("The replay of match {MatchId} is no longer available at Valve", matchId);
            }
            result = SavedReplayAnalysis.Expired(matchId, timeProvider.GetUtcNow());
        }
        catch (ReplayUnreadableException ex)
        {
            logger.LogWarning(
                ex, "Could not parse the replay of match {MatchId}; it is tried again after {RetryInterval}",
                matchId, GetMatchAnalysisHandler.FailedReplayRetryInterval);
            result = SavedReplayAnalysis.Failed(matchId, timeProvider.GetUtcNow());
        }
        catch (ReplayParserBusyException ex)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(ex, "The replay parser is busy; match {MatchId} was not parsed this time", matchId);
            }
            return ParseReplayResult.ParserBusy;
        }
        catch (ReplayParserUnavailableException ex)
        {
            logger.LogWarning(ex, "The replay parser is unavailable; match {MatchId} was not parsed", matchId);
            return ParseReplayResult.ParserUnavailable;
        }

        await TrySaveAsync(result, cancellationToken);
        return result.State switch
        {
            SavedReplayState.Ready => ParseReplayResult.Ready,
            SavedReplayState.Expired => ParseReplayResult.Expired,
            _ => ParseReplayResult.Failed,
        };
    }

    private async Task TrySaveAsync(SavedReplayAnalysis result, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveAsync(result, cancellationToken);
        }
        catch (StorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not save the replay result of match {MatchId} to the database", result.MatchId);
        }
    }
}

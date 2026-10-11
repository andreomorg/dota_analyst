using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Abstractions;

// Provides the full data of a match (today from OpenDota; later also from the Go replay parser).
// It only delivers data: interpreting it is the Domain's job (e.g. PerformanceEvaluator).
public interface IReplayAnalyzer
{
    /// <returns>The match, or null when it does not exist.</returns>
    /// <exception cref="DataSourceUnavailableException">The data source could not be reached.</exception>
    Task<MatchDetails?> GetMatchDetailsAsync(MatchId matchId, CancellationToken cancellationToken);
}

using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Abstractions;

// A match the collector could add to the comparison base.
public record BaselineCandidate(MatchId MatchId, BaselineSource Source);

// Where candidate matches come from (OpenDota's lists of pro and high-rank public matches).
public interface IBaselineMatchSource
{
    // Recent matches old enough for their replay to be on Valve's servers, newest first. High-rank matches only
    // with at least one of focusHeroes (pro matches: all of them, they are few).
    /// <exception cref="DataSourceUnavailableException">The source could not be reached.</exception>
    Task<IReadOnlyList<BaselineCandidate>> GetCandidatesAsync(
        BaselineSource source, IReadOnlySet<int> focusHeroes, CancellationToken cancellationToken);
}

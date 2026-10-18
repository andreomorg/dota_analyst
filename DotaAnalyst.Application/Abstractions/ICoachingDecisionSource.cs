using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Abstractions;

public interface ICoachingDecisionSource
{
    Task<CoachingDecisionContext?> FindAsync(MatchId matchId, int heroId, CancellationToken cancellationToken);
}

using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Coaching;

public sealed class ReviewCoachingHandler(ICoachingDecisionSource source, ICoachingReviewProvider provider)
{
    public async Task<CoachingReviewRequest?> GetContextAsync(MatchId matchId, int heroId, CancellationToken cancellationToken)
    {
        var context = await source.FindAsync(matchId, heroId, cancellationToken);
        return context is null ? null : PrepareCoachingReview.Build(context);
    }

    public async Task<CoachingReviewResult?> ReviewAsync(MatchId matchId, int heroId, CancellationToken cancellationToken)
    {
        var context = await source.FindAsync(matchId, heroId, cancellationToken);
        return context is null ? null : await provider.ReviewAsync(context, cancellationToken);
    }
}

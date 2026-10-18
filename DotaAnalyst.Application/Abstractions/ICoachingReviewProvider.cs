using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Application.Abstractions;

public interface ICoachingReviewProvider
{
    Task<CoachingReviewResult> ReviewAsync(CoachingDecisionContext context, CancellationToken cancellationToken);
}

using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Application.Abstractions;

public interface ICoachingHypothesisProvider
{
    Task<CoachingHypothesisReport> GenerateAsync(CoachingDecisionContext context, CancellationToken cancellationToken);
}

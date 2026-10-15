using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Application.Abstractions;

public interface ICoachingProvider
{
    Task<CoachingAdvice> GenerateAsync(CoachingContext context, CancellationToken cancellationToken);
}

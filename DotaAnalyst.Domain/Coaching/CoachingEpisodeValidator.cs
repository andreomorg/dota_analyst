namespace DotaAnalyst.Domain.Coaching;

public static class CoachingEpisodeValidator
{
    public static bool IsValid(CoachingContext context)
    {
        if (context.Episodes is null || context.Episodes.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id))) return false;
        if (context.Episodes.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != context.Episodes.Count) return false;
        return context.Episodes.All(e =>
            e.HeroId == context.HeroId && e.FromSeconds >= 0 && e.FromSeconds <= e.EventSeconds
            && e.EventSeconds <= e.ToSeconds && e.ToSeconds <= context.DurationSeconds
            && e.PositionCoverage is { ExpectedChecks: > 0, ObservedChecks: >= 0 } coverage
            && coverage.ObservedChecks <= coverage.ExpectedChecks
            && (e.Kind switch
            {
                CoachingEpisodeKind.Death => e.NearFight is null && coverage.ExpectedChecks == 4
                    && (e.IsolatedDeath is null || e.IsolatedDeath == true && coverage.IsComplete
                        || e.IsolatedDeath == false && coverage.ObservedChecks > 0),
                CoachingEpisodeKind.DetectedFight => e.IsolatedDeath is null && !e.Reincarnated
                    && coverage.ExpectedChecks == (long)e.ToSeconds - e.FromSeconds + 1
                    && (e.NearFight is null || e.NearFight == false && coverage.IsComplete
                        || e.NearFight == true && coverage.ObservedChecks > 0),
                _ => false
            })
            && (e.Economy is null || e.Economy.FromSeconds >= 0
                && e.Economy.FromSeconds <= e.EventSeconds && e.EventSeconds < e.Economy.ToSeconds
                && e.Economy.ToSeconds <= context.DurationSeconds && e.Economy.LastHitsChange is not < 0));
    }
}

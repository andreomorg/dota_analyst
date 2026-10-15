namespace DotaAnalyst.Domain.Coaching;

// Version 4 facts describe only bounded episodes; legacy application metrics keep their original meaning.
public static class CoachingEpisodeFacts
{
    public static IReadOnlyList<CoachingEvidence> Build(IReadOnlyList<CoachingEpisode> episodes, int durationSeconds)
    {
        var deaths = episodes.Where(e => e.Kind == CoachingEpisodeKind.Death).ToArray();
        var fights = episodes.Where(e => e.Kind == CoachingEpisodeKind.DetectedFight).ToArray();
        CoachingEvidence Count(string metric, int count) => new(
            "coverage-" + metric, metric, count, "count", 0, durationSeconds,
            "CoachingEpisodeBuilder", "Bounded sampled episodes; unknown coverage is not isolation or distance. No tactical verdict.");
        return [Count("ReviewedDeaths", deaths.Length),
            Count("ConfirmedIsolatedDeaths", deaths.Count(e => e.IsolatedDeath == true)),
            Count("UnknownIsolationDeaths", deaths.Count(e => e.IsolatedDeath is null)),
            Count("ReviewedFights", fights.Length),
            Count("ConfirmedDistantFights", fights.Count(e => e.NearFight == false)),
            Count("UnknownFightProximity", fights.Count(e => e.NearFight is null))];
    }

    public static bool Matches(CoachingContext context) => context.Episodes is not null
        && Build(context.Episodes, context.DurationSeconds).All(expected =>
            context.Evidence.Count(e => e.Metric == expected.Metric) == 1
            && context.Evidence.Single(e => e.Metric == expected.Metric) == expected);
}

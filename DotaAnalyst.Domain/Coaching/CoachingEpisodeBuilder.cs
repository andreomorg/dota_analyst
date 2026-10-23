using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;
using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Domain.Coaching;

public static class CoachingEpisodeBuilder
{
    private const int PositionToleranceSeconds = 1;
    private const int DeathWindowSeconds = 15;
    private const int ExpectedAllies = 4;

    public static IReadOnlyList<CoachingEpisode> Build(
        ReplayAnalysis replay, int heroId, IReadOnlyList<HeroPositions>? positions)
    {
        if (replay.Timeline is not { } timeline || replay.FindHero(heroId) is not { } player) return [];
        var duration = timeline.Seconds[^1];
        var positionsOf = (positions ?? []).DistinctBy(p => p.HeroId).ToDictionary(p => p.HeroId);
        var allies = replay.Players.Where(p => p.Team == player.Team && p.HeroId != heroId)
            .DistinctBy(p => p.HeroId).ToArray();
        var deaths = (replay.Deaths ?? []).Where(d => d.Seconds >= 0 && d.Seconds <= duration).ToArray();
        var episodes = new List<CoachingEpisode>();
        foreach (var (death, index) in deaths.Select((death, index) => (death, index)).Where(pair => pair.death.HeroId == heroId))
        {
            var samples = allies.Select(ally => positionsOf.GetValueOrDefault(ally.HeroId)?.At(death.Seconds, PositionToleranceSeconds))
                .Where(sample => sample is { } p && p.Seconds >= 0 && p.Seconds <= duration).Select(sample => sample!.Value).ToArray();
            var coverage = new CoachingPositionCoverage(ExpectedAllies, samples.Length);
            var allyNear = samples.Any(p => Distance(p.X, p.Y, death.X, death.Y) <= MatchMetricsCalculator.AllyRadius);
            bool? isolated = allyNear ? false : coverage.IsComplete && allies.Length == ExpectedAllies ? true : null;
            episodes.Add(new($"death-{heroId}-{death.Seconds}-{index}", CoachingEpisodeKind.Death,
                heroId, death.Seconds, Math.Max(0, death.Seconds - DeathWindowSeconds),
                death.Seconds + Math.Min(DeathWindowSeconds, duration - death.Seconds), coverage,
                isolated, null, death.Reincarnated, EconomyAt(timeline, heroId, death.Seconds)));
        }

        var teamOf = replay.Players.ToDictionary(p => p.HeroId, p => p.Team);
        var fights = TeamfightDetector.Detect(deaths, teamOf);
        var hero = positionsOf.GetValueOrDefault(heroId);
        foreach (var (fight, index) in fights.Select((fight, index) => (fight, index)))
        {
            var from = Math.Max(0, fight.StartSeconds - MatchMetricsCalculator.FightLeadInSeconds);
            var to = Math.Min(duration, fight.EndSeconds);
            var samples = hero?.Samples.Where(p => p.Seconds >= from && p.Seconds <= to).ToArray() ?? [];
            // Per-second coverage checks with the same tolerance as the existing sampled-position calculations.
            var inWindow = new HeroPositions(heroId, samples);
            var observed = Enumerable.Range(from, to - from + 1).Count(second => inWindow.At(second, PositionToleranceSeconds) is not null);
            var coverage = new CoachingPositionCoverage(to - from + 1, observed);
            var centerX = fight.Deaths.Average(d => d.X);
            var centerY = fight.Deaths.Average(d => d.Y);
            var wasNear = samples.Any(p => Distance(p.X, p.Y, centerX, centerY) <= MatchMetricsCalculator.FightPresenceRadius);
            bool? near = wasNear ? true : coverage.IsComplete ? false : null;
            episodes.Add(new($"fight-{heroId}-{fight.StartSeconds}-{fight.EndSeconds}-{index}",
                CoachingEpisodeKind.DetectedFight, heroId, fight.StartSeconds, from, to, coverage,
                null, near, false, EconomyAt(timeline, heroId, fight.StartSeconds)));
        }
        return episodes.OrderBy(e => e.EventSeconds).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
    }

    private static double Distance(double x, double y, double otherX, double otherY) =>
        new MapPoint(x, y).DistanceTo(new MapPoint(otherX, otherY));

    private static CoachingEconomyInterval? EconomyAt(MatchTimeline timeline, int heroId, int seconds)
    {
        if (timeline.FindHero(heroId) is not { } series) return null;
        for (var i = 1; i < timeline.Seconds.Count; i++)
        {
            if (timeline.Seconds[i - 1] > seconds || seconds >= timeline.Seconds[i]) continue;
            int? lastHits = series.Series.TryGetValue(StatKey.LastHits, out var hits) && hits[i] >= hits[i - 1]
                ? hits[i] - hits[i - 1] : null;
            return new(timeline.Seconds[i - 1], timeline.Seconds[i], series.NetWorth[i] - series.NetWorth[i - 1], lastHits);
        }
        return null;
    }
}

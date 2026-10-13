using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Domain service: the HeroMetrics of every hero of a match, from what the replay has (positions every few seconds,
// deaths, wards, the timeline and the laning numbers).
public static class MatchMetricsCalculator
{
    // How far past the river (the diagonal x + y = 0, in map units) the enemy's side of the map starts.
    public const int RiverMargin = 1000;

    // An ally hero this close (map units) counts as nearby.
    public const int AllyRadius = 1500;

    // Positions come every 2 s, so any second has a sample at most 1 s away while the hero is alive; a wider window
    // would count an ally who had just died as still there.
    private const int SampleTolerance = 1;

    // roles: role-queue position (1-5) of each hero; null when the match has no role queue (Turbo, unranked) or a
    // hero lacks it: then there are no metrics (they are compared by role).
    public static IReadOnlyList<HeroMetrics>? Calculate(
        IReadOnlyList<LaningStats> players,
        MatchTimeline timeline,
        IReadOnlyList<HeroDeath> deaths,
        IReadOnlyList<WardPlacement> wards,
        IReadOnlyList<HeroPositions> positions,
        IReadOnlyDictionary<int, int>? roles)
    {
        if (roles is null || players.Any(player => roles.GetValueOrDefault(player.HeroId) is < 1 or > 5))
        {
            return null;
        }

        var positionsOf = positions.DistinctBy(hero => hero.HeroId).ToDictionary(hero => hero.HeroId);
        var matchEnd = timeline.Seconds[^1];

        return players.Select(player =>
        {
            var allies = players
                .Where(other => other.Team == player.Team && other.HeroId != player.HeroId)
                .Select(other => positionsOf.GetValueOrDefault(other.HeroId))
                .OfType<HeroPositions>()
                .ToList();
            bool AllyNear(int seconds, int x, int y) => allies.Any(ally =>
                ally.At(seconds, SampleTolerance) is { } sample && Distance(sample.X, sample.Y, x, y) <= AllyRadius);

            var heroDeaths = deaths.Where(death => death.HeroId == player.HeroId).ToList();
            var samples = positionsOf.GetValueOrDefault(player.HeroId)?.Samples.Where(sample => sample.Seconds >= 0).ToList() ?? [];
            var onEnemySide = samples.Where(sample => IsEnemySide(player.Team, sample.X, sample.Y)).ToList();
            var alone = onEnemySide.Count(sample => !AllyNear(sample.Seconds, sample.X, sample.Y));
            // Each point classified once, then counted per region.
            var timeRegions = samples
                .GroupBy(sample => MapRegions.RegionOf(player.Team, sample.X, sample.Y))
                .ToDictionary(group => group.Key, group => group.Count());
            var deathRegions = heroDeaths
                .GroupBy(death => MapRegions.RegionOf(player.Team, death.X, death.Y))
                .ToDictionary(group => group.Key, group => group.Count());

            var series = timeline.FindHero(player.HeroId);
            var teamNetWorth = timeline.Heroes.Where(hero => hero.Team == player.Team).Sum(hero => hero.NetWorth[^1]);

            var placed = wards.Where(ward => ward.PlacedByHeroId == player.HeroId).ToList();
            var observers = placed.Where(ward => ward.Kind == WardKind.Observer).ToList();

            return new HeroMetrics(
                HeroId: player.HeroId,
                Team: player.Team,
                Role: roles[player.HeroId],
                Deaths: heroDeaths.Count,
                IsolatedDeaths: heroDeaths.Count(death => !AllyNear(death.Seconds, death.X, death.Y)),
                DeathsOnEnemySide: heroDeaths.Count(death => IsEnemySide(player.Team, death.X, death.Y)),
                EnemySideShare: Share(onEnemySide.Count, samples.Count),
                AloneOnEnemySideShare: Share(alone, samples.Count),
                NetWorthAt10: series is null ? null : ValueAt(timeline, series.NetWorth, 600),
                NetWorthAt20: series is null ? null : ValueAt(timeline, series.NetWorth, 1200),
                LastHitsAt10: player.LastHits,
                NetWorthShare: series is null ? 0 : Share(series.NetWorth[^1], teamNetWorth),
                ObserversPlaced: observers.Count,
                SentriesPlaced: placed.Count(ward => ward.Kind == WardKind.Sentry),
                ObserverAverageLifetimeSeconds: observers.Count == 0
                    ? null
                    : (int)Math.Round(observers.Average(ward => (ward.RemovedAt ?? matchEnd) - ward.PlacedAt)),
                WardsLostToEnemy: placed.Count(ward => ward.Destroyed),
                EnemyWardsDestroyed: wards.Count(ward => ward.Team != player.Team && ward.DestroyedByHeroId == player.HeroId),
                TimeByRegion: PerRegion<double>.Of(region => Share(timeRegions.GetValueOrDefault(region), samples.Count)),
                DeathsByRegion: PerRegion<int>.Of(region => deathRegions.GetValueOrDefault(region)));
        }).ToList();
    }

    // More than RiverMargin past the river, on the other team's side (Radiant's side is x + y < 0).
    public static bool IsEnemySide(Team team, int x, int y)
    {
        var pastRiver = (x + y) / Math.Sqrt(2);
        return team == Team.Radiant ? pastRiver > RiverMargin : pastRiver < -RiverMargin;
    }

    private static double Distance(int x1, int y1, int x2, int y2) => Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));

    private static double Share(int part, int whole) => whole == 0 ? 0 : (double)part / whole;

    private static int? ValueAt(MatchTimeline timeline, IReadOnlyList<int> values, int seconds)
    {
        var index = timeline.Seconds.ToList().IndexOf(seconds);
        return index >= 0 ? values[index] : null;
    }
}

using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// Where a hero stood and died by the real terrain of the patch (TerrainZones, MP5), from 0:00. Shares are of the
// position samples alive (every 2 s); counts are deaths (Aegis deaths included, like HeroMetrics.Deaths).
// - Own / enemy high ground: terrain level 2+ on the hero's / the enemy's side of the map (the bases included).
// - River level: terrain level 0 (the river and the low corners by it), not the pits.
// - Narrow ground: less than 70% of the ground within TerrainZones.OpennessRadius walkable without standing trees.
// - Deep enemy jungle: the enemy's jungle with at least DeepJungleTrees standing trees around.
// - Below enemy high ground: on terrain lower than an enemy high ground within BelowHighGroundReach (the classic death
//   walking up a ramp or under a cliff).
// The shares are null without samples from 0:00 (nothing to divide by). Narrow ground and deep jungle depend on the
// trees standing at each moment (timelines.trees): null without them.
public sealed record TerrainMetrics(
    double? OwnHighGroundShare,
    double? EnemyHighGroundShare,
    double? RiverLevelShare,
    double? NarrowGroundShare,
    double? DeepEnemyJungleShare,
    int DeathsOnEnemyHighGround,
    int DeathsBelowEnemyHighGround,
    int? DeathsInNarrowGround,
    int? DeathsInDeepEnemyJungle);

// Domain service: the TerrainMetrics of one hero.
public static class TerrainMetricsCalculator
{
    // Openness below this is narrow ground, and at least this many standing trees within TerrainZones.TreeRadius in the
    // enemy jungle its deep part: about the tightest and the most wooded 10% of the positions of two 7.41 replays
    // (23/10/2026: openness p5 0.65, p10 0.72; trees within 300 top 10% ≥ 4). Deaths happen in tighter spots than
    // positions overall (openness of deaths p5 0.56).
    public const double NarrowOpenness = 0.7;

    public const int DeepJungleTrees = 4;

    // How far an enemy high ground may be for a lower point to count as below it.
    public const int BelowHighGroundReach = 400;

    public static TerrainMetrics Calculate(
        GameMap map, Team team, IReadOnlyList<PositionSample> samples, IReadOnlyList<HeroDeath> deaths, TreeTimeline? trees = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var alive = samples.Where(sample => sample.Seconds >= 0).OrderBy(sample => sample.Seconds).ToList();
        var downTrees = trees is null ? null : new TreeSweep(trees);
        var zones = alive.Select(sample => TerrainZones.Classify(map, sample.X, sample.Y, downTrees?.At(sample.Seconds))).ToList();
        double? Share(Func<TerrainZone, bool> where) => zones.Count == 0 ? null : (double)zones.Count(where) / zones.Count;
        double? TreeShare(Func<TerrainZone, bool> where) => trees is null ? null : Share(where);

        // A second sweep for the deaths (their own order); without the trees, the zone counts every tree standing and is
        // only used for what does not depend on them.
        var deathSweep = trees is null ? null : new TreeSweep(trees);
        var deathZones = deaths.OrderBy(death => death.Seconds)
            .Select(death => (Death: death, Zone: TerrainZones.Classify(map, death.X, death.Y, deathSweep?.At(death.Seconds))))
            .ToList();
        int? TreeCount(Func<TerrainZone, bool> where) => trees is null ? null : deathZones.Count(entry => where(entry.Zone));

        return new TerrainMetrics(
            OwnHighGroundShare: Share(zone => zone.HighGround && zone.Side == team),
            EnemyHighGroundShare: Share(zone => zone.HighGround && zone.Side != team),
            RiverLevelShare: Share(zone => zone.Level == 0 && zone.Area != TerrainArea.RoshanPit),
            NarrowGroundShare: TreeShare(IsNarrow),
            DeepEnemyJungleShare: TreeShare(zone => IsDeepEnemyJungle(zone, team)),
            DeathsOnEnemyHighGround: deathZones.Count(entry => entry.Zone.HighGround && entry.Zone.Side != team),
            DeathsBelowEnemyHighGround: deathZones.Count(entry => !entry.Zone.HighGround
                && BelowEnemyHighGround(map, team, entry.Death.X, entry.Death.Y, entry.Zone.Level)),
            DeathsInNarrowGround: TreeCount(IsNarrow),
            DeathsInDeepEnemyJungle: TreeCount(zone => IsDeepEnemyJungle(zone, team)));
    }

    private static bool IsNarrow(TerrainZone zone) => zone.Openness < NarrowOpenness;

    private static bool IsDeepEnemyJungle(TerrainZone zone, Team team) =>
        zone.Area == TerrainArea.Jungle && zone.Side != team && zone.TreesNearby >= DeepJungleTrees;

    // A higher enemy high ground (level 2+, on the enemy's side) at one of 8 points BelowHighGroundReach away.
    private static bool BelowEnemyHighGround(GameMap map, Team team, int x, int y, int? level)
    {
        if (level is not { } own)
        {
            return false;
        }

        for (var step = 0; step < 8; step++)
        {
            var angle = step * Math.PI / 4;
            double px = x + BelowHighGroundReach * Math.Cos(angle), py = y + BelowHighGroundReach * Math.Sin(angle);
            if (map.LevelAt(px, py) is { } other && other >= 2 && other > own && TerrainZones.Classify(map, px, py).Side != team)
            {
                return true;
            }
        }

        return false;
    }

    // The trees down at increasing seconds, applying each change once (TreeTimeline.DownAt rebuilds the set every call:
    // too slow for every position sample). Ask in non-decreasing order.
    private sealed class TreeSweep(TreeTimeline trees)
    {
        private readonly HashSet<int> down = [.. trees.DeadAtStart];
        private int next;

        public IReadOnlySet<int> At(int second)
        {
            for (; next < trees.Changes.Count && trees.Changes[next].Seconds <= second; next++)
            {
                if (trees.Changes[next].Alive)
                {
                    down.Remove(trees.Changes[next].Index);
                }
                else
                {
                    down.Add(trees.Changes[next].Index);
                }
            }

            return down;
        }
    }
}

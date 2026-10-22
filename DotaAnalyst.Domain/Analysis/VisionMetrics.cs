using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// What the fog of war showed around a hero, from the game's own visibility on every position sample (every 2 s,
// HeroVitals.VisibleToEnemies), from 0:00. A counter is null if any relevant event cannot be evaluated; zero means
// all events were evaluated or there were none. Non-hero killers are evaluated as not coming from fog.
// Windows require at least window / 2 distinct samples with known visibility, first within 2 s of the start, last
// within 2 s of the event, and no gap greater than 2 s. No missing samples are tolerated. A previous death within
// the window (including reincarnation) invalidates it: the samples must belong to the current life.
// - Seen by enemies: share of the samples alive the enemy team saw the hero.
// - Deaths while tracked: the hero was seen on every sample of the TrackedSeconds before dying (the enemy watched it
//   come).
// - Deaths unseen before: the hero was seen on no sample of the TrackedSeconds before dying (the enemy found it without
//   having it in sight: a gank from the fog, a smoke, the hero walking into them).
// - Deaths to a killer from the fog: the killer hero was seen by the hero's team on no sample of the FogSeconds before.
// - Kills from the fog: the same, the other way: the hero killed an enemy whose team had not seen it in FogSeconds.
// - Observer seen share: of the walkable ground within an observer ward's range, the part it really saw by the map of
//   the patch (terrain, the trees standing when it was placed, fog blockers: VisionModel with VisionRules.Measured),
//   averaged over the hero's observers. A ward in a low spot or behind trees sees little of its circle. Null without
//   observers, without the patch's map or without the trees of the match.
public sealed record VisionMetrics(
    double SeenByEnemiesShare,
    int? DeathsWhileTracked,
    int? DeathsUnseenBefore,
    int? DeathsToKillerFromFog,
    int? KillsFromFog,
    double? ObserverSeenShare = null);

// Domain service: the VisionMetrics of one hero.
public static class VisionMetricsCalculator
{
    public const int TrackedSeconds = 10;

    public const int FogSeconds = 6;

    // Observer wards' range in 7.41 (npc_units.txt, day and night), and the spacing of the points checked inside it.
    public const double ObserverRange = 1600;

    public const int CoverageStep = 128;

    // positionsOf: every hero's samples (the killers' and victims' too); deaths: all the match's hero deaths; wards: all
    // the match's wards. Null when the hero's samples have no visibility (documents from before it).
    public static VisionMetrics? Calculate(
        int heroId, IReadOnlyDictionary<int, HeroPositions> positionsOf, IReadOnlyList<HeroDeath> deaths,
        IReadOnlyList<WardPlacement>? wards = null, GameMap? map = null, TreeTimeline? trees = null)
    {
        if (positionsOf.GetValueOrDefault(heroId) is not { } own)
        {
            return null;
        }

        var known = own.Samples.Where(sample => sample.Seconds >= 0 && sample.Vitals?.VisibleToEnemies is not null).ToList();
        if (known.Count == 0)
        {
            return null;
        }

        var heroDeaths = deaths.Where(death => death.HeroId == heroId).ToList();
        var tracks = heroDeaths.Select(death => Seen(own, death.Seconds, TrackedSeconds, deaths)).ToList();
        return new VisionMetrics(
            SeenByEnemiesShare: (double)known.Count(sample => sample.Vitals!.VisibleToEnemies == true) / known.Count,
            DeathsWhileTracked: CountEvaluated(tracks.Select(track => track is null ? (bool?)null : track.All(seen => seen))),
            DeathsUnseenBefore: CountEvaluated(tracks.Select(track => track is null ? (bool?)null : !track.Any(seen => seen))),
            DeathsToKillerFromFog: CountEvaluated(heroDeaths.Select(death => death.Killer != KillerKind.Hero
                ? false : FromFog(death.KillerHeroId, death.Seconds, positionsOf, deaths))),
            KillsFromFog: CountEvaluated(deaths.Where(death => death.KillerHeroId == heroId && death.HeroId != heroId)
                .Select(death => FromFog(heroId, death.Seconds, positionsOf, deaths))),
            ObserverSeenShare: map is null || trees is null ? null : ObserverSeenShare(heroId, wards ?? [], map, trees));
    }

    private static double? ObserverSeenShare(int heroId, IReadOnlyList<WardPlacement> wards, GameMap map, TreeTimeline trees)
    {
        var shares = wards.Where(ward => ward.Kind == WardKind.Observer && ward.PlacedByHeroId == heroId)
            .Select(ward => SeenShare(map, ward.X, ward.Y, trees.DownAt(ward.PlacedAt)))
            .OfType<double>()
            .ToList();
        return shares.Count == 0 ? null : shares.Average();
    }

    // The part of the walkable points within ObserverRange (every CoverageStep) a ward at (x, y) sees; null when none is
    // walkable (a position off the map).
    public static double? SeenShare(GameMap map, int x, int y, IReadOnlySet<int> downTrees)
    {
        ArgumentNullException.ThrowIfNull(map);
        var ward = new VisionSource(x, y, ObserverRange);
        int walkable = 0, seen = 0;
        var steps = (int)Math.Ceiling(ObserverRange / CoverageStep);
        for (var i = -steps; i <= steps; i++)
        {
            for (var j = -steps; j <= steps; j++)
            {
                double px = x + i * CoverageStep, py = y + j * CoverageStep;
                if ((double)(i * CoverageStep) * (i * CoverageStep) + (double)(j * CoverageStep) * (j * CoverageStep)
                    > ObserverRange * ObserverRange || !map.IsWalkable(px, py))
                {
                    continue;
                }

                walkable++;
                if (VisionModel.Sees(map, ward, px, py, downTrees, VisionRules.Measured))
                {
                    seen++;
                }
            }
        }

        return walkable == 0 ? null : (double)seen / walkable;
    }

    // Unknown events invalidate the total rather than contributing a measured zero.
    private static int? CountEvaluated(IEnumerable<bool?> events)
    {
        var count = 0;
        foreach (var result in events)
        {
            if (result is null)
            {
                return null;
            }

            if (result == true)
            {
                count++;
            }
        }

        return count;
    }

    // Whether the killer was unseen throughout a sufficiently covered window of its current life.
    private static bool? FromFog(int? killerHeroId, int seconds, IReadOnlyDictionary<int, HeroPositions> positionsOf,
        IReadOnlyList<HeroDeath> deaths) =>
        killerHeroId is { } killer && positionsOf.GetValueOrDefault(killer) is { } track
        && Seen(track, seconds, FogSeconds, deaths) is { } seen ? !seen.Any(visible => visible) : null;

    // Visibility in [seconds - window, seconds); null without full temporal coverage.
    private static List<bool>? Seen(HeroPositions track, int seconds, int window, IReadOnlyList<HeroDeath> deaths)
    {
        var start = seconds - window;
        var samples = track.Samples.Where(sample => sample.Seconds >= start && sample.Seconds < seconds)
            .OrderBy(sample => sample.Seconds).ToList();
        if (samples.Select(sample => sample.Seconds).Distinct().Count() < window / 2
            || samples.Any(sample => sample.Vitals?.VisibleToEnemies is null || sample.Vitals.Health is <= 0)
            || samples[0].Seconds > start + 2 || samples[^1].Seconds < seconds - 2
            || samples.Zip(samples.Skip(1), (left, right) => right.Seconds - left.Seconds).Any(gap => gap > 2)
            || deaths.Any(death => death.HeroId == track.HeroId && death.Seconds >= start && death.Seconds < seconds))
        {
            return null;
        }

        return samples.Select(sample => sample.Vitals!.VisibleToEnemies!.Value).ToList();
    }
}

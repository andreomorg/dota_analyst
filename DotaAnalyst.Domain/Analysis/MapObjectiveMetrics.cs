using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// A hero's own counters of the map's objectives at the end of the match, as the game keeps them (players[].final of the
// replay): runes by kind (bounty, power, water), Wisdom Shrines (XP runes), Healing Lotuses picked, Watchers activated,
// outposts captured, Tormentors last hit, neutral camps stacked.
public sealed record MapObjectiveCounts(
    int BountyRunes,
    int PowerRunes,
    int WaterRunes,
    int WisdomShrines,
    int Lotuses,
    int Watchers,
    int Outposts,
    int TormentorKills,
    int CampsStacked);

// Where an objective was taken: the place Roshan or a Tormentor died, or where the tower stood.
public sealed record ObjectivePlace(MapObjective Objective, int X, int Y);

// A Wisdom Shrine (XP rune) a hero took (the replay's rune pickups of type 8, checked against the game's own counter
// on 30 of 30 players, OB1), and the side of the map of that shrine (null: unknown).
public sealed record WisdomTake(int HeroId, Team TakenBy, int Seconds, Team? ShrineSide);

// The map's objectives for one hero: its counters (null when the replay lacks them) and how many of the objectives its
// team took it was there for — within PresenceRadius at the moment, like a fight (MatchMetricsCalculator): Roshan and
// Tormentor kills, enemy towers 1-3 (not denied). A "nearby" count is null when the team took none of that kind or the
// place of one of them is unknown.
// Wisdom Shrines of the team's side the enemy took, and of the enemy's side the team took (the whole team's, like
// TeamObserverUptime: the shrines are a team matter). Null without the pickups or when the side of one is unknown.
public sealed record MapObjectiveMetrics(
    MapObjectiveCounts? Counts,
    int RoshanKills,
    int? RoshanKillsNearby,
    int TormentorKills,
    int? TormentorKillsNearby,
    int EnemyTowersTaken,
    int? EnemyTowersTakenNearby,
    int? OwnShrinesLost = null,
    int? EnemyShrinesTaken = null);

// Domain service: the MapObjectiveMetrics of one hero.
public static class MapObjectiveMetricsCalculator
{
    public const int PresenceRadius = 1500;

    // Valve's DOTA_RUNES number of the Wisdom Shrine's XP rune (OB1: equal to the game's shrine counter on 30 of 30).
    public const int WisdomRuneType = 8;

    // How far from the objective's second a position sample may be (samples are every 2 s).
    public const int SampleTolerance = 2;

    // heroDeaths: the hero's own deaths (a dead hero has no position samples: dead at the moment = not there).
    public static MapObjectiveMetrics Calculate(
        Team team, HeroPositions? positions, IReadOnlyList<MapObjective> objectives, IReadOnlyList<ObjectivePlace>? places,
        MapObjectiveCounts? counts, IReadOnlyList<WisdomTake>? wisdom = null, IReadOnlyList<HeroDeath>? heroDeaths = null)
    {
        var shrinesKnown = wisdom is not null && wisdom.All(take => take.ShrineSide is not null);
        var taken = objectives.Where(objective => objective.TakenBy == team).ToList();
        var roshan = taken.Where(objective => objective.Kind == ObjectiveKind.Roshan).ToList();
        var tormentors = taken.Where(objective => objective.Kind == ObjectiveKind.Tormentor).ToList();
        var towers = taken.Where(objective => objective is { Kind: ObjectiveKind.Tower, Denied: false, TowerTier: <= 3 }).ToList();

        // All or nothing: one objective without a known place, or with the hero's whereabouts unknown then, makes the
        // count unknown (not a smaller share).
        int? Nearby(List<MapObjective> list)
        {
            if (list.Count == 0)
            {
                return null;
            }

            var near = list.Select(objective => (places ?? []).FirstOrDefault(place => place.Objective == objective) is { } place
                ? WasNear(positions, heroDeaths ?? [], place)
                : null).ToList();
            return near.Any(value => value is null) ? null : near.Count(value => value == true);
        }

        return new MapObjectiveMetrics(counts, roshan.Count, Nearby(roshan), tormentors.Count, Nearby(tormentors),
            towers.Count, Nearby(towers),
            OwnShrinesLost: shrinesKnown ? wisdom!.Count(take => take.TakenBy != team && take.ShrineSide == team) : null,
            EnemyShrinesTaken: shrinesKnown ? wisdom!.Count(take => take.TakenBy == team && take.ShrineSide != team) : null);
    }

    // The side of the Wisdom Shrine a hero took at (x, y): the side of the nearest shrine of the map (by the diagonal,
    // like TerrainZones), when it is within ShrineReach (the position sample can be 2 s off the pickup); null otherwise.
    public const int ShrineReach = 1000;

    public static Team? ShrineSideAt(GameMap map, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(map);
        var shrine = map.Landmarks.Where(landmark => landmark.Kind == LandmarkKind.WisdomShrine)
            .MinBy(landmark => new MapPoint(landmark.X, landmark.Y).DistanceSquaredTo(new MapPoint(x, y)));
        return shrine is not null && new MapPoint(shrine.X, shrine.Y).IsWithin(new MapPoint(x, y), ShrineReach)
            ? shrine.X + shrine.Y < 0 ? Team.Radiant : Team.Dire
            : null;
    }

    // Whether the hero was within PresenceRadius when the objective was taken, by its sample closest to that second
    // (within SampleTolerance; the earlier one on a tie, so a Blink or teleport just after does not count). No sample:
    // false when the hero was dead then (died before, no sample since), unknown otherwise (a gap in the replay).
    private static bool? WasNear(HeroPositions? positions, IReadOnlyList<HeroDeath> heroDeaths, ObjectivePlace place)
    {
        if (positions is null)
        {
            return null;
        }

        var seconds = place.Objective.Seconds;
        var sample = Enumerable.Range(0, SampleTolerance + 1)
            .SelectMany(offset => new[] { seconds - offset, seconds + offset })
            .Select(second => positions.At(second, tolerance: 0))
            .FirstOrDefault(found => found is not null);
        if (sample is { } at)
        {
            return new MapPoint(at.X, at.Y).IsWithin(new MapPoint(place.X, place.Y), PresenceRadius);
        }

        var lastDeath = heroDeaths.Where(death => death.Seconds <= seconds).MaxBy(death => death.Seconds);
        return lastDeath is not null && !positions.Samples.Any(s => s.Seconds > lastDeath.Seconds && s.Seconds <= seconds)
            ? false
            : null;
    }
}

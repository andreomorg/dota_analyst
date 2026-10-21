using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// The hero's condition (health, mana, states) and what its team saw: the enemies in sight and the ones missing from the
// map (the game's own fog of war, HeroVitals.VisibleToEnemies).
public sealed partial class HeroSituationBuilder
{
    // An enemy unseen for this long is "missing"; its last sighting is looked for this far back.
    public const int MissingAfterSeconds = 10;
    public const int LastSeenLookbackSeconds = 120;

    private static Condition ConditionOf(PositionSample self, HeroVitals vitals, List<PositionSample> allies, List<PositionSample> enemies)
    {
        // An enemy the team saw is one whose "visible to its enemies" is true; unknown for any enemy = unknown for all.
        var seen = enemies.Any(enemy => enemy.Vitals?.VisibleToEnemies is null)
            ? null
            : enemies.Where(enemy => enemy.Vitals!.VisibleToEnemies == true).Select(enemy => Distance(self, enemy)).ToList();
        return new Condition(vitals.Health, vitals.HealthShare, vitals.ManaShare,
            LowestShare(allies.Where(ally => Distance(self, ally) <= FightRange)), vitals.Gold,
            vitals.Stunned, vitals.Silenced, vitals.Invisible,
            VisibleEnemies: seen?.Count,
            VisibleEnemiesNear: seen?.Count(distance => distance <= NearRadius),
            VisibleEnemiesWithinFightRange: seen?.Count(distance => distance <= FightRange),
            NearestVisibleEnemyDistance: seen is { Count: > 0 } ? seen.Min() : null);
    }

    private static double? LowestShare(IEnumerable<PositionSample> heroes) =>
        heroes.Select(hero => hero.Vitals?.HealthShare).OfType<double>().Select(share => (double?)share).Min();

    private static double? AverageShare(IEnumerable<PositionSample> heroes)
    {
        var shares = heroes.Select(hero => hero.Vitals?.HealthShare).OfType<double>().ToList();
        return shares.Count == 0 ? null : shares.Average();
    }

    private MissingEnemies MissingOf(Team team, PositionSample self, int seconds)
    {
        var count = 0;
        (int Distance, int SecondsAgo)? nearest = null;
        foreach (var enemy in teamOf.Where(hero => hero.Value != team).Select(hero => hero.Key))
        {
            if (PositionAt(enemy, seconds) is not { } now)
            {
                continue; // dead: not missing
            }

            if (now.Vitals?.VisibleToEnemies is not { } seenNow)
            {
                return MissingEnemies.Unknown; // a document without visibility
            }

            if (seenNow)
            {
                continue;
            }

            // The enemy's samples are every 2 s while alive: walk back to its last sighting by this team.
            var track = positionsOf[enemy];
            PositionSample? lastSeen = null;
            for (var back = 1; back <= LastSeenLookbackSeconds && lastSeen is null; back++)
            {
                if (track.At(seconds - back, tolerance: 0) is { Vitals.VisibleToEnemies: true } sample)
                {
                    lastSeen = sample;
                }
            }

            if (lastSeen is { } seen && seconds - seen.Seconds < MissingAfterSeconds)
            {
                continue; // just out of sight
            }

            count++;
            if (lastSeen is { } spot && (nearest is null || Distance(self, spot) < nearest.Value.Distance))
            {
                nearest = (Distance(self, spot), seconds - spot.Seconds);
            }
        }

        return new MissingEnemies(count, nearest?.Distance, nearest?.SecondsAgo);
    }
}

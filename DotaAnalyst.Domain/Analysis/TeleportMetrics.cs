using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Domain.Analysis;

// A hero's teleports (TP Scroll, Boots of Travel: the replay's modifier_teleporting, BuffKind.Teleport), from 0:00.
// - Completed: the hero's position jumped more than ArrivalJump between the last sample by the channel's end and the
//   first one up to ArrivalSeconds after it (the channel can end without a teleport: cancelled or interrupted).
// - Into fights: completed teleports whose arrival is within FightReach of a fight (TeamfightDetector) going on or
//   starting up to FightSeconds after the arrival — the TP that joined the fight.
// A teleport whose outcome can't be told (no end, no samples around it) makes all three counts unknown (null), never a
// smaller count; null too for documents without the buffs.
public sealed record TeleportMetrics(int? Teleports, int? Cancelled, int? IntoFights);

// Domain service: the TeleportMetrics of one hero.
public static class TeleportMetricsCalculator
{
    public const int ArrivalJump = 1500;

    public const int ArrivalSeconds = 4;

    public const int FightReach = 2500;

    public const int FightSeconds = 20;

    public static TeleportMetrics? Calculate(
        int heroId, IReadOnlyList<Buff>? buffs, HeroPositions? positions, IReadOnlyList<Teamfight> fights,
        IReadOnlyList<HeroDeath> heroDeaths)
    {
        if (buffs is null)
        {
            return null;
        }

        var outcomes = buffs.Where(buff => buff.Kind == BuffKind.Teleport && buff.TargetHeroId == heroId && buff.Start >= 0)
            .Select(buff => Outcome(buff, positions, heroDeaths))
            .ToList();
        if (outcomes.Any(outcome => outcome is null))
        {
            return new TeleportMetrics(null, null, null);
        }

        var arrivals = outcomes.Select(outcome => outcome!.Value).Where(outcome => outcome.Completed).ToList();
        return new TeleportMetrics(
            arrivals.Count,
            outcomes.Count - arrivals.Count,
            arrivals.Count(arrival => fights.Any(fight => IntoFight(arrival, fight))));
    }

    // Completed or not, with the arrival; null when unknown. A hero that died during the channel (or right at its end)
    // did not teleport: cancelled, and known.
    private static (bool Completed, int Seconds, int X, int Y)? Outcome(Buff teleport, HeroPositions? positions, IReadOnlyList<HeroDeath> heroDeaths)
    {
        if (teleport.End is not { } end || positions is null)
        {
            return null;
        }

        if (heroDeaths.Any(death => death.Seconds >= teleport.Start && death.Seconds <= end + 1))
        {
            return (false, (int)end, 0, 0);
        }

        // Before: at least a second before the end (a sample on the end's own second can already be at the destination).
        var before = positions.Samples.Where(sample => sample.Seconds <= end - 1).Select(sample => (PositionSample?)sample).LastOrDefault();
        var after = positions.Samples.Where(sample => sample.Seconds >= end && sample.Seconds <= end + ArrivalSeconds)
            .Select(sample => (PositionSample?)sample).FirstOrDefault();
        if (before is not { } from || after is not { } to)
        {
            return null;
        }

        var jumped = !new MapPoint(to.X, to.Y).IsWithin(new MapPoint(from.X, from.Y), ArrivalJump);
        return (jumped, to.Seconds, to.X, to.Y);
    }

    private static bool IntoFight((bool Completed, int Seconds, int X, int Y) arrival, Teamfight fight)
    {
        if (fight.EndSeconds < arrival.Seconds || fight.StartSeconds > arrival.Seconds + FightSeconds)
        {
            return false;
        }

        double centerX = fight.Deaths.Average(death => death.X), centerY = fight.Deaths.Average(death => death.Y);
        return new MapPoint(arrival.X, arrival.Y).IsWithin(new MapPoint(centerX, centerY), FightReach);
    }
}

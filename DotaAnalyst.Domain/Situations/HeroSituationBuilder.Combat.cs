using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// The fight around the hero (hero-vs-hero hits) and its recent farm (creep last hits).
public sealed partial class HeroSituationBuilder
{
    // Who struck first: the hits of the last 10 s; the farm: the creep last hits of the last minute.
    public const int FightStartWindowSeconds = 10;
    public const int FarmWindowSeconds = 60;
    // Disables received: the last 10 s.
    public const int AfflictionWindowSeconds = 10;

    private Afflictions AfflictionsOf(int heroId, int seconds)
    {
        if (activity?.Debuffs is not { } debuffs)
        {
            return Afflictions.Unknown;
        }

        var mine = debuffs.Where(debuff => debuff.TargetHeroId == heroId && debuff.Start <= seconds).ToList();
        var active = mine.Where(debuff => debuff.ActiveAt(seconds)).ToList();
        var windowStart = seconds - AfflictionWindowSeconds;
        // Time disabled in (windowStart, seconds]: the union of the disables (a stun and a silence at once count once),
        // each cut at this second (its real end may be later: the future).
        var intervals = mine.Where(debuff => debuff.IsDisable)
            .Select(debuff => (From: Math.Max(debuff.Start, windowStart), To: Math.Min(debuff.End ?? debuff.Start, seconds)))
            .Where(interval => interval.To > interval.From)
            .OrderBy(interval => interval.From);
        double disabled = 0, reached = windowStart;
        foreach (var (from, to) in intervals)
        {
            disabled += Math.Max(0, to - Math.Max(from, reached));
            reached = Math.Max(reached, to);
        }
        return new Afflictions(
            Disables: active.Count(debuff => debuff.IsDisable),
            Slows: active.Count(debuff => debuff.Kind == DebuffKind.Slow),
            Others: active.Count(debuff => !debuff.IsDisable && debuff.Kind != DebuffKind.Slow),
            DisabledSeconds: Math.Round(disabled, 1),
            EnemiesAfflicting: mine.Where(debuff => debuff.Start > windowStart && debuff.SourceHeroId is not null)
                .Select(debuff => debuff.SourceHeroId).Distinct().Count());
    }

    private Boons BoonsOf(int heroId, int seconds)
    {
        if (activity?.Buffs is not { } buffs)
        {
            return Boons.Unknown;
        }

        var mine = buffs.Where(buff => buff.TargetHeroId == heroId && buff.Start <= seconds).ToList();
        var active = mine.Where(buff => buff.ActiveAt(seconds)).ToList();
        return new Boons(
            Teleporting: active.Any(buff => buff.Kind == BuffKind.Teleport),
            MagicImmune: active.Any(buff => buff.Kind == BuffKind.MagicImmune),
            Saved: active.Any(buff => buff.Kind == BuffKind.Save),
            Heals: active.Count(buff => buff.Kind == BuffKind.Heal),
            SpeedBuffs: active.Count(buff => buff.Kind == BuffKind.Speed),
            AlliesBuffing: mine.Where(buff => buff.FromAlly && buff.Start > seconds - AfflictionWindowSeconds)
                .Select(buff => buff.SourceHeroId).Distinct().Count());
    }

    private Fight FightOf(int heroId, int seconds)
    {
        if (activity?.HeroHits is not { } hits)
        {
            return Fight.Unknown;
        }

        var recent = hits.Where(hit => hit.EndsBy(seconds) && hit.Seconds >= seconds - HitWindowSeconds).ToList();
        var mine = hits.Where(hit => hit.EndsBy(seconds) && hit.Seconds >= seconds - FightStartWindowSeconds
            && (hit.AttackerHeroId == heroId || hit.TargetHeroId == heroId)).ToList();
        bool? struckFirst = null;
        if (mine.Count > 0)
        {
            var first = mine.Where(hit => hit.Seconds == mine.Min(other => other.Seconds)).ToList();
            var hitFirst = first.Any(hit => hit.AttackerHeroId == heroId);
            var hitBack = first.Any(hit => hit.TargetHeroId == heroId);
            struckFirst = hitFirst == hitBack ? null : hitFirst;
        }

        return new Fight(
            EnemiesHittingMe: recent.Where(hit => hit.TargetHeroId == heroId).Select(hit => hit.AttackerHeroId).Distinct().Count(),
            EnemiesIHit: recent.Where(hit => hit.AttackerHeroId == heroId).Select(hit => hit.TargetHeroId).Distinct().Count(),
            IStruckFirst: struckFirst);
    }

    // The camps of a jungle (seen from `team`) stacked / empty at this second; null without camps in the document.
    private (int? Stacked, int? Empty) Camps(Team team, MapRegion jungle, int seconds)
    {
        if (activity?.NeutralCamps is not { } camps)
        {
            return (null, null);
        }

        var here = camps.Where(camp => MapRegions.RegionOf(team, camp.X, camp.Y) == jungle).ToList();
        return (here.Count(camp => camp.StackedAt(seconds)), here.Count(camp => camp.AliveAt(seconds) == 0));
    }

    private Farm FarmOf(int heroId, Team team, int seconds)
    {
        var (stacked, empty) = Camps(team, MapRegion.OwnJungle, seconds);
        if (activity?.CreepKills is not { } kills)
        {
            return Farm.Unknown with { OwnCampsStacked = stacked, OwnCampsEmpty = empty };
        }

        var recent = kills.Where(kill => kill.HeroId == heroId && kill.Seconds <= seconds && kill.Seconds > seconds - FarmWindowSeconds)
            .ToList();
        return new Farm(
            LaneLastHits: recent.Count(kill => kill.Kind == CreepKillKind.Lane),
            Neutrals: recent.Count(kill => kill.Kind == CreepKillKind.Neutral),
            Denies: recent.Count(kill => kill.Kind == CreepKillKind.Deny),
            SecondsSinceLastCreepKill: recent.Count == 0 ? null : (int)Math.Floor(seconds - recent.Max(kill => kill.Seconds)),
            OwnCampsStacked: stacked,
            OwnCampsEmpty: empty);
    }
}

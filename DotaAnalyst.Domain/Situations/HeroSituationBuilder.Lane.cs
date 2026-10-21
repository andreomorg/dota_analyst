using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Domain.Situations;

// The lane matchup: the hero against its direct opponent (the enemy in the opposing position, LaneMatchup.OpposingRole;
// positions from RoleOf: the role queue's or estimated at this second, so no future either way).
public sealed partial class HeroSituationBuilder
{
    public const int LaneTradeWindowSeconds = 60;

    private LaneOpponent LaneOpponentOf(int heroId, Team team, PositionSample self, int seconds)
    {
        if (RoleOf(heroId, seconds).Role is not { } role || LaneMatchup.OpposingRole(role) is not { } opposing)
        {
            return LaneOpponent.Unknown;
        }

        var opponent = teamOf.Where(hero => hero.Value != team).Select(hero => (int?)hero.Key)
            .FirstOrDefault(enemy => RoleOf(enemy!.Value, seconds).Role == opposing);
        if (opponent is not { } them)
        {
            return LaneOpponent.Unknown;
        }

        var at = PositionAt(them, seconds);
        var deaths = replay.Deaths!.Where(death => death.Seconds <= seconds).ToList();
        var hits = activity?.HeroHits?.Where(hit => hit.EndsBy(seconds) && hit.Seconds >= seconds - LaneTradeWindowSeconds).ToList();
        return new LaneOpponent(
            HeroId: them,
            IAmMelee: heroDetails?.GetValueOrDefault(heroId)?.AttackType is { } myAttack ? myAttack == HeroAttackType.Melee : null,
            OpponentMelee: heroDetails?.GetValueOrDefault(them)?.AttackType is { } attack ? attack == HeroAttackType.Melee : null,
            LevelLead: Value(heroId, StatKey.Level, seconds) - Value(them, StatKey.Level, seconds),
            KillsOnMe: deaths.Count(death => death.HeroId == heroId && death.KillerHeroId == them),
            MyKillsOnThem: deaths.Count(death => death.HeroId == them && death.KillerHeroId == heroId),
            SeenDistance: at is { Vitals.VisibleToEnemies: true } seen ? Distance(self, seen) : null,
            DamageToThem: hits?.Where(hit => hit.AttackerHeroId == heroId && hit.TargetHeroId == them).Sum(hit => hit.Amount),
            DamageFromThem: hits?.Where(hit => hit.AttackerHeroId == them && hit.TargetHeroId == heroId).Sum(hit => hit.Amount),
            HiddenDistance: at is { } where ? Distance(self, where) : null,
            HiddenNetWorthLead: Value(heroId, StatKey.NetWorth, seconds) - Value(them, StatKey.NetWorth, seconds));
    }
}

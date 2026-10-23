using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Maps;

namespace DotaAnalyst.Domain.Situations;

// The hero's income by source, the team's pings, the spells traded and the rune clock.
public sealed partial class HeroSituationBuilder
{
    public const int EconomyWindowSeconds = 60;
    public const int SignalWindowSeconds = 10;
    // A teammate's ping this close to the hero is about it (or where it stands).
    public const int PingNearRadius = 2500;
    // Power runes: every 2 minutes from 6:00 (current patch; the clock is all the player needs).
    public const int FirstPowerRuneSeconds = 360;
    public const int PowerRuneEverySeconds = 120;

    private Signals SignalsOf(int heroId, Team team, PositionSample self, int seconds)
    {
        int? Income(EconomyResource resource, params EconomySource[] sources) =>
            activity?.Economy is { } economy
                ? economy.Where(bucket => bucket.HeroId == heroId && bucket.Resource == resource && bucket.EndsBy(seconds)
                        && bucket.Seconds >= seconds - EconomyWindowSeconds && (sources.Length == 0 || sources.Contains(bucket.Source)))
                    .Sum(bucket => bucket.Amount)
                : null;

        var recentPings = activity?.Pings?.Where(ping => ping.Team == team && ping.Seconds <= seconds && ping.Seconds > seconds - SignalWindowSeconds)
            .ToList();
        // Only uses whose target the replay linked: null targets say nothing (activity.AbilityUses always exists).
        var recentSpells = activity?.AbilityUses.Where(use => use.TargetHeroId is not null && use.Seconds <= seconds
            && use.Seconds > seconds - SignalWindowSeconds).ToList();
        var targetsKnown = activity?.AbilityUses.Any(use => use.TargetHeroId is not null || use.TargetX is not null) == true;

        return new Signals(
            GoldFromCreeps: Income(EconomyResource.Gold, EconomySource.Creeps),
            GoldFromNeutrals: Income(EconomyResource.Gold, EconomySource.Neutrals),
            GoldFromHeroes: Income(EconomyResource.Gold, EconomySource.Heroes),
            GoldFromObjectives: Income(EconomyResource.Gold, EconomySource.Objectives),
            ExperienceGained: Income(EconomyResource.Experience),
            AllyPingsNear: recentPings?.Count(ping => ping.HeroId != heroId
                && new MapPoint(ping.X, ping.Y).IsWithin(new MapPoint(self.X, self.Y), PingNearRadius)),
            OwnPings: recentPings?.Count(ping => ping.HeroId == heroId),
            EnemySpellsOnMe: targetsKnown ? recentSpells!.Count(use => use.TargetHeroId == heroId && teamOf.GetValueOrDefault(use.HeroId) != team) : null,
            MySpellsOnEnemies: targetsKnown
                ? recentSpells!.Count(use => use.HeroId == heroId && use.TargetHeroId is { } target && teamOf.GetValueOrDefault(target) != team)
                : null,
            SecondsToNextPowerRune: seconds < FirstPowerRuneSeconds
                ? FirstPowerRuneSeconds - seconds
                : (PowerRuneEverySeconds - (seconds - FirstPowerRuneSeconds) % PowerRuneEverySeconds) % PowerRuneEverySeconds);
    }

    public const int RecentDeathsWindowSeconds = 300;
    private const int UltimateLevel = 6;

    private Momentum MomentumOf(int heroId, Team team, int seconds)
    {
        var deaths = replay.Deaths!.Where(death => death.Seconds <= seconds).ToList();
        var lastDeath = deaths.Where(death => death.HeroId == heroId).Select(death => (int?)death.Seconds).Max() ?? int.MinValue;
        bool Ours(int hero) => teamOf.GetValueOrDefault(hero) == team;
        var lastMinute = deaths.Where(death => death.Seconds > seconds - 60 && death.KillerHeroId is not null).ToList();
        return new Momentum(
            KillStreak: deaths.Count(death => death.KillerHeroId == heroId && death.Seconds > lastDeath && !Ours(death.HeroId)),
            RecentDeaths: deaths.Count(death => death.HeroId == heroId && !death.Reincarnated && death.Seconds > seconds - RecentDeathsWindowSeconds),
            TeamKillsLastMinute: lastMinute.Count(death => !Ours(death.HeroId) && Ours(death.KillerHeroId!.Value)),
            EnemyKillsLastMinute: lastMinute.Count(death => Ours(death.HeroId) && !Ours(death.KillerHeroId!.Value)),
            EnemiesWithUltimateLevel: teamOf.Count(hero => hero.Value != team && Value(hero.Key, Stats.StatKey.Level, seconds) >= UltimateLevel),
            SecondsSinceMyBigItem: BigItems(seconds)?.Where(purchase => purchase.HeroId == heroId).Select(purchase => (int?)(seconds - purchase.Seconds)).Min());
    }

    // A big item: not a consumable nor a recipe, costing at least this (the same threshold as FirstBigItemSeconds).
    public const int BigItemCost = 2000;
    public const int EnemyItemWindowSeconds = 120;

    // The big items bought up to this second (an assembled item is logged as its own purchase); null without the
    // item catalog or the purchases.
    private IEnumerable<ItemPurchase>? BigItems(int seconds) =>
        itemDetails is null || replay.Purchases is null
            ? null
            : replay.Purchases.Where(purchase => purchase.Seconds <= seconds && itemDetails.TryGetValue(purchase.ItemKey, out var item)
                && !item.IsConsumable && !item.IsRecipe && item.Cost >= BigItemCost);

    private int? EnemyBigItems(Team team, int seconds) =>
        BigItems(seconds)?.Count(purchase => teamOf.TryGetValue(purchase.HeroId, out var owner) && owner != team
            && purchase.Seconds > seconds - EnemyItemWindowSeconds);

    // A power rune lies on the map now: its last change up to this second was "appeared" (null without the data).
    private bool? PowerRuneOnMap(int seconds)
    {
        if (activity?.RunesOnMap is not { } runes)
        {
            return null;
        }

        return runes.Where(rune => rune.Seconds <= seconds && RunePickup.IsPowerType(rune.RuneType))
            .GroupBy(rune => (rune.X, rune.Y))
            .Any(spot => spot.Last().Present);
    }
}

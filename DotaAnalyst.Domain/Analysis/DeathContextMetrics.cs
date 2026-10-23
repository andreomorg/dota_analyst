using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// What each of a hero's deaths looked like at the moment it happened ("why did I die?"), counted over the match:
// - EscapeItemReady: an escape or save item (EscapeItems) in the inventory, with no cooldown running, a second before
//   the death — it died with the way out unused. BkbReady: the same for Black King Bar alone.
// - ToThreeOrMore: the killer hero and the assisting heroes were 3 or more (caught by a gank or lost a fight).
// - WhileDisabled: a disable (stun, hex, silence, root) was on it at the death; DisabledSecondsBefore: time disabled in
//   the DisabledWindowSeconds before the death (stuns and silences at once count once), summed over the deaths.
// Each count is null when the replay lacks what it needs (inventories and item cooldowns; debuffs), never a guess.
public sealed record DeathContextMetrics(
    int Deaths,
    int? EscapeItemReady,
    int? BkbReady,
    int ToThreeOrMore,
    int? WhileDisabled,
    double? DisabledSecondsBefore);

// Domain service: the DeathContextMetrics of one hero.
public static class DeathContextMetricsCalculator
{
    public const int DisabledWindowSeconds = 10;

    public const int GankSize = 3;

    public const string BlackKingBar = "item_black_king_bar";

    // Internal item names (items.slots) that move the hero away or save it from a fight.
    public static readonly IReadOnlySet<string> EscapeItems = new HashSet<string>
    {
        "item_blink", "item_overwhelming_blink", "item_swift_blink", "item_arcane_blink", "item_force_staff",
        "item_hurricane_pike", "item_glimmer_cape", BlackKingBar, "item_ghost", "item_cyclone",
        "item_wind_waker", "item_manta", "item_shadow_amulet", "item_invis_sword", "item_silver_edge",
        "item_lotus_orb", "item_sphere", "item_aeon_disk",
    };

    public static DeathContextMetrics Calculate(int heroId, IReadOnlyList<HeroDeath> heroDeaths, ReplayActivity? activity)
    {
        var deaths = heroDeaths.Where(death => death.HeroId == heroId).ToList();
        var items = activity is { SlotChanges: { } slots, ItemCooldowns: { } cooldowns }
            ? deaths.Select(death => HeldItems.ReadyAt(heroId, death.Seconds - 1, slots, cooldowns)).ToList()
            : null;
        var debuffs = activity?.Debuffs?.Where(debuff => debuff.TargetHeroId == heroId && debuff.IsDisable).ToList();

        return new DeathContextMetrics(
            Deaths: deaths.Count,
            EscapeItemReady: items?.Count(ready => ready.Overlaps(EscapeItems)),
            BkbReady: items?.Count(ready => ready.Contains(BlackKingBar)),
            ToThreeOrMore: deaths.Count(death => Attackers(death) >= GankSize),
            WhileDisabled: debuffs is null ? null : deaths.Count(death => debuffs.Any(debuff => debuff.ActiveAt(death.Seconds))),
            DisabledSecondsBefore: debuffs is null ? null : Math.Round(deaths.Sum(death => DisabledBefore(death.Seconds, debuffs)), 1));
    }

    // The enemy heroes who took part: the killer (only a hero kill names one) and the assists.
    private static int Attackers(HeroDeath death) =>
        (death.KillerHeroId is { } killer ? death.AssistHeroIds.Append(killer) : death.AssistHeroIds).Distinct().Count();

    // Seconds disabled in the window before the death: the union of the disables, each cut to the window.
    private static double DisabledBefore(int deathSecond, IReadOnlyList<Debuff> disables)
    {
        var windowStart = deathSecond - DisabledWindowSeconds;
        var intervals = disables
            .Select(debuff => (From: Math.Max(debuff.Start, windowStart), To: Math.Min(debuff.End ?? debuff.Start, deathSecond)))
            .Where(interval => interval.To > interval.From)
            .OrderBy(interval => interval.From);
        double disabled = 0, reached = windowStart;
        foreach (var (from, to) in intervals)
        {
            disabled += Math.Max(0, to - Math.Max(from, reached));
            reached = Math.Max(reached, to);
        }

        return disabled;
    }
}

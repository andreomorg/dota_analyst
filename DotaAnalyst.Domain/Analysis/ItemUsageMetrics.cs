using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// How a hero used its fight items in the team fights it was in (TeamfightDetector, near the fight's center):
// - KeyItemReady: fights it started holding a ready key item (KeyItems: initiation, escape, saves); KeyItemUsed: of those,
//   the fights where it used one of them between the lead-in and the fight's end.
// - BkbReady / BkbUsed: the same for Black King Bar alone.
// "Ready" = in the inventory or neutral slot with no cooldown running at the fight's start. Each count is null when the
// replay lacks the inventories or item cooldowns (unknown, not "never used").
public sealed record ItemUsageMetrics(int Fights, int? KeyItemReady, int? KeyItemUsed, int? BkbReady, int? BkbUsed);

// Domain service: the ItemUsageMetrics of one hero.
public static class ItemUsageMetricsCalculator
{
    // Seconds before a fight's first death in which an item use still counts as for the fight (initiation, Blink in).
    public const int LeadInSeconds = 10;

    // Items whose activation decides fights: get in, get out, survive, save. Internal names (items.slots / items.uses).
    public static readonly IReadOnlySet<string> KeyItems = new HashSet<string>
    {
        "item_black_king_bar", "item_blink", "item_overwhelming_blink", "item_swift_blink", "item_arcane_blink",
        "item_force_staff", "item_hurricane_pike", "item_glimmer_cape", "item_ghost", "item_cyclone", "item_wind_waker",
        "item_lotus_orb", "item_shadow_amulet", "item_invis_sword", "item_silver_edge", "item_manta", "item_sheepstick",
        "item_orchid", "item_bloodthorn", "item_abyssal_blade", "item_nullifier", "item_refresher", "item_satanic",
        "item_mjollnir", "item_pipe", "item_crimson_guard", "item_guardian_greaves", "item_mekansm", "item_solar_crest",
    };

    // fights: the team fights the hero was in.
    public static ItemUsageMetrics Calculate(int heroId, IReadOnlyList<Teamfight> fights, ReplayActivity? activity)
    {
        if (activity is not { SlotChanges: { } slots, ItemCooldowns: { } cooldowns })
        {
            return new ItemUsageMetrics(fights.Count, null, null, null, null);
        }

        var uses = activity.ItemUses.Where(use => use.HeroId == heroId).ToList();
        int keyReady = 0, keyUsed = 0, bkbReady = 0, bkbUsed = 0;
        foreach (var fight in fights)
        {
            var start = fight.StartSeconds - LeadInSeconds;
            var ready = HeldItems.ReadyAt(heroId, start, slots, cooldowns);
            var used = uses.Where(use => use.Seconds >= start && use.Seconds <= fight.EndSeconds).Select(use => use.Name).ToHashSet();
            if (ready.Overlaps(KeyItems))
            {
                keyReady++;
                if (used.Overlaps(KeyItems)) keyUsed++;
            }

            if (ready.Contains(DeathContextMetricsCalculator.BlackKingBar))
            {
                bkbReady++;
                if (used.Contains(DeathContextMetricsCalculator.BlackKingBar)) bkbUsed++;
            }
        }

        return new ItemUsageMetrics(fights.Count, keyReady, keyUsed, bkbReady, bkbUsed);
    }
}

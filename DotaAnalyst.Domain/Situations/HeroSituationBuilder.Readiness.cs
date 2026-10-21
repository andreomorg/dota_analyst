using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Situations;

// What the hero had ready: teleport, escape items, the ultimate and the abilities on cooldown (inventory changes and
// cooldowns of ReplayActivity).
public sealed partial class HeroSituationBuilder
{
    // Internal item names (items.uses) that move the hero or save it from a fight. Only what they are, not whether the
    // charge was back: the recharge is not in the replay data we read.
    public static readonly IReadOnlySet<string> TeleportItems = new HashSet<string>
    {
        "item_tpscroll", "item_travel_boots", "item_travel_boots_2",
    };

    public static readonly IReadOnlySet<string> EscapeItems = new HashSet<string>
    {
        "item_blink", "item_overwhelming_blink", "item_swift_blink", "item_arcane_blink", "item_force_staff",
        "item_hurricane_pike", "item_glimmer_cape", "item_black_king_bar", "item_ghost", "item_cyclone",
        "item_wind_waker", "item_manta", "item_shadow_amulet", "item_invis_sword", "item_silver_edge",
        "item_lotus_orb", "item_sphere", "item_aeon_disk",
    };

    private Readiness ReadinessOf(int heroId, int seconds)
    {
        if (ItemsAt(heroId, seconds) is not { } items || activity!.AbilityCooldowns is not { } abilityCooldowns)
        {
            return Readiness.Unknown;
        }

        var (slots, cooling) = items;
        bool Ready(string item) => !cooling.Contains(item);

        var inventory = Inventory(slots);
        var teleports = inventory.Where(TeleportItems.Contains)
            .Concat(slots.GetValueOrDefault(SlotChange.TeleportSlot) is { } scroll ? [scroll] : []).ToList();
        var escapes = inventory.Where(EscapeItems.Contains).ToList();

        var abilities = abilityCooldowns.Where(cooldown => cooldown.HeroId == heroId && cooldown.Seconds <= seconds).ToList();
        var ultimate = abilities.MaxBy(cooldown => cooldown.Length)?.Name;
        return new Readiness(
            TeleportReady: teleports.Any(Ready),
            HasEscapeItem: escapes.Count > 0,
            EscapeItemReady: escapes.Any(Ready),
            UltimateReady: ultimate is null ? null : !abilities.Any(cooldown => cooldown.Name == ultimate && cooldown.ActiveAt(seconds)),
            AbilitiesOnCooldown: abilities.Where(cooldown => cooldown.ActiveAt(seconds)).Select(cooldown => cooldown.Name).Distinct().Count());
    }

    // The item in each slot at this second (the last change at or before it: the reading at this very second is the
    // present, not the future) and the items with a cooldown running. Null without inventories/cooldowns.
    private (Dictionary<int, string?> Slots, HashSet<string> Cooling)? ItemsAt(int heroId, int seconds)
    {
        if (activity?.SlotChanges is not { } changes || activity.ItemCooldowns is not { } itemCooldowns)
        {
            return null;
        }

        var slots = changes.Where(change => change.HeroId == heroId && change.Seconds <= seconds)
            .GroupBy(change => change.Slot)
            .ToDictionary(group => group.Key, group => group.Last().Item);
        var cooling = itemCooldowns.Where(cooldown => cooldown.HeroId == heroId && cooldown.ActiveAt(seconds))
            .Select(cooldown => cooldown.Name).ToHashSet();
        return (slots, cooling);
    }

    private static List<string> Inventory(Dictionary<int, string?> slots) =>
        Enumerable.Range(0, SlotChange.InventorySlots).Select(slot => slots.GetValueOrDefault(slot)).OfType<string>().ToList();
}

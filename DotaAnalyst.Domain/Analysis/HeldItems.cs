using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// The items a hero could activate at a second, from the replay's inventory changes and item cooldowns.
internal static class HeldItems
{
    // The items in the inventory and the neutral slot at this second (the last change at or before it; the backpack and
    // the stash can't be used) with no cooldown running.
    public static HashSet<string> ReadyAt(int heroId, int seconds, IReadOnlyList<SlotChange> changes, IReadOnlyList<CooldownStart> cooldowns)
    {
        var held = changes.Where(change => change.HeroId == heroId && change.Seconds <= seconds
                && (change.Slot < SlotChange.InventorySlots || change.Slot == SlotChange.NeutralSlot))
            .GroupBy(change => change.Slot)
            .Select(group => group.Last().Item)
            .OfType<string>();
        var cooling = cooldowns.Where(cooldown => cooldown.HeroId == heroId && cooldown.ActiveAt(seconds))
            .Select(cooldown => cooldown.Name).ToHashSet();
        return held.Where(item => !cooling.Contains(item)).ToHashSet();
    }
}

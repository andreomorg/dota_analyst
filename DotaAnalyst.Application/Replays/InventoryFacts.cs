using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

/// <summary>A hero's items at a second, from the inventory changes read every 2 s (the last change at or before it, never
/// a later one), each place kept apart: the 6 usable inventory slots, the backpack (carried, not usable), the stash (not
/// with the hero), teleport, neutral artifact and enhancement. Null slot: empty. Backpack is Unavailable in documents
/// that do not track it (before DATA-D16: no backpack change anywhere in the match) — unknown, not empty.</summary>
public sealed record InventoryAt(
    FactAvailability Availability, FactAvailability Backpack, int Seconds,
    IReadOnlyList<string?>? Inventory, IReadOnlyList<string?>? BackpackSlots, IReadOnlyList<string?>? Stash,
    string? Teleport, string? Neutral, string? Enhancement);

// Read-only inventory facts for the internal factual consumer (D18).
public static class InventoryFacts
{
    public static InventoryAt At(ReplayActivity? activity, int heroId, int seconds)
    {
        if (activity?.SlotChanges is not { } changes)
        {
            return new InventoryAt(FactAvailability.Unavailable, FactAvailability.Unavailable, seconds, null, null, null, null, null, null);
        }

        var slots = changes.Where(change => change.HeroId == heroId && change.Seconds <= seconds)
            .GroupBy(change => change.Slot)
            .ToDictionary(group => group.Key, group => group.Last().Item);
        IReadOnlyList<string?> Range(int first, int count) =>
            Enumerable.Range(first, count).Select(slot => slots.GetValueOrDefault(slot)).ToList();
        var backpackTracked = changes.Any(change => change.IsBackpack);

        return new InventoryAt(
            FactAvailability.Available,
            backpackTracked ? FactAvailability.Available : FactAvailability.Unavailable,
            seconds,
            Range(0, SlotChange.InventorySlots),
            backpackTracked ? Range(SlotChange.FirstBackpackSlot, SlotChange.BackpackSlots) : null,
            Range(SlotChange.FirstStashSlot, SlotChange.StashSlots),
            slots.GetValueOrDefault(SlotChange.TeleportSlot), slots.GetValueOrDefault(SlotChange.NeutralSlot),
            slots.GetValueOrDefault(SlotChange.EnhancementSlot));
    }
}

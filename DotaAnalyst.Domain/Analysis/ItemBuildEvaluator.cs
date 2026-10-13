using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

// An item bought before the horn, with how many were bought (3 Iron Branches).
public record StartingItem(string ItemKey, int Count);

// An item of the build and when it got into the inventory: bought whole, or assembled from its parts.
public record BuildItem(string ItemKey, int Seconds, bool Assembled);

public record ItemBuild(
    IReadOnlyList<StartingItem> StartingItems, IReadOnlyList<BuildItem> Items, int? FirstBigItemSeconds = null);

// Domain service: the item build of one hero from its purchases. The replay logs an assembled item (Black King Bar)
// as a purchase of its own when its parts come together, after the purchases of the parts and the recipe; the item
// details tell which items are assembled and which parts each one used up. Approximate: items sold, disassembled or
// given to someone else still count for the buyer.
public static class ItemBuildEvaluator
{
    // An item bought whole is part of the build from this cost up (Blink Dagger, Bottle, Aghanim's Shard), unless it
    // later became a part of another item (Point Booster -> Aghanim's Scepter).
    public const int MinBoughtCost = 600;
    public const int MinBigItemCost = 2000;

    // purchases: the hero's purchases (in any order). catalog: the items by key; purchases of unknown keys never show.
    public static ItemBuild Evaluate(IEnumerable<ItemPurchase> purchases, IReadOnlyDictionary<string, ItemDetails> catalog)
    {
        // OrderBy is stable: purchases in the same second keep the replay's order (parts before the item they make).
        var ordered = purchases.OrderBy(purchase => purchase.Seconds).ToList();

        // Which purchases became a part of a later item: each assembled item uses up the latest earlier purchase of
        // each of its components that wasn't used up yet.
        var usedUp = new HashSet<int>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!catalog.TryGetValue(ordered[i].ItemKey, out var item))
            {
                continue;
            }

            foreach (var component in item.Components)
            {
                var part = Enumerable.Range(0, i).Reverse()
                    .FirstOrDefault(j => !usedUp.Contains(j) && ordered[j].ItemKey == component, -1);
                if (part >= 0)
                {
                    usedUp.Add(part);
                }
            }
        }

        var starting = ordered
            .Where(purchase => purchase.Seconds < 0)
            .GroupBy(purchase => purchase.ItemKey)
            .Select(group => new StartingItem(group.Key, group.Count()))
            .ToList();

        var items = ordered
            .Select((purchase, index) => (Purchase: purchase, Index: index))
            .Where(entry => catalog.TryGetValue(entry.Purchase.ItemKey, out var details)
                && IsBuildItem(entry.Purchase, details, usedUp.Contains(entry.Index)))
            .Select(entry => new BuildItem(
                entry.Purchase.ItemKey, entry.Purchase.Seconds, Assembled: catalog[entry.Purchase.ItemKey].Components.Count > 0))
            .ToList();

        var firstBigItemSeconds = items
            .FirstOrDefault(item => catalog[item.ItemKey].Cost >= MinBigItemCost)
            ?.Seconds;
        return new ItemBuild(starting, items, firstBigItemSeconds);
    }

    // Assembled items always show, even when they later became a part (Mekansm -> Guardian Greaves: a step of the
    // build); items bought whole only when they are expensive and stayed whole.
    private static bool IsBuildItem(ItemPurchase purchase, ItemDetails details, bool becamePart) =>
        !details.IsConsumable && !details.IsRecipe
        && (details.Components.Count > 0 || (purchase.Seconds >= 0 && details.Cost >= MinBoughtCost && !becamePart));
}

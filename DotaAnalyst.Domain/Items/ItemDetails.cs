namespace DotaAnalyst.Domain.Items;

// Value object: the game data of an item that the item build needs.
public record ItemDetails
{
    // The prefix of a recipe's key: recipe_black_king_bar is the recipe of black_king_bar.
    public const string RecipePrefix = "recipe_";

    public ItemDetails(string key, int cost, IReadOnlyList<string>? components, bool isConsumable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfNegative(cost);
        if (components is not null && components.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A component is an item key.", nameof(components));
        }

        Key = key;
        Cost = cost;
        Components = components?.ToList() ?? [];
        IsConsumable = isConsumable;
    }

    // Internal name without the "item_" prefix (black_king_bar), the way the replay names items.
    public string Key { get; }

    // Gold cost (of the whole item, recipe included).
    public int Cost { get; }

    // Keys of the items it is made of; the same key can repeat (Magic Wand: magic_stick, branches, branches) and it
    // can include a recipe. Empty for an item that is only bought.
    public IReadOnlyList<string> Components { get; }

    // Tango, wards, TP scrolls...: never part of a build.
    public bool IsConsumable { get; }

    public bool IsRecipe => Key.StartsWith(RecipePrefix, StringComparison.Ordinal);

    // A record compares lists by reference; items with the same components are the same item.
    public virtual bool Equals(ItemDetails? other) =>
        other is not null && Key == other.Key && Cost == other.Cost && IsConsumable == other.IsConsumable
        && Components.SequenceEqual(other.Components);

    public override int GetHashCode() => HashCode.Combine(Key, Cost, IsConsumable);
}

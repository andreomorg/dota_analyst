namespace DotaAnalyst.Domain.Replays;

// Value object: an item a hero bought, read from the replay. Recipes are purchases too (recipe_wraith_band), and so
// is an assembled item, logged when its parts come together (see ItemBuildEvaluator).
public record ItemPurchase
{
    public ItemPurchase(int heroId, int seconds, string itemKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKey);

        HeroId = heroId;
        Seconds = seconds;
        ItemKey = itemKey;
    }

    public int HeroId { get; }

    // Game clock of the purchase (negative before the horn: the starting items).
    public int Seconds { get; }

    // Internal name without the "item_" prefix (black_king_bar), the same key as ItemDetails.Key.
    public string ItemKey { get; }
}

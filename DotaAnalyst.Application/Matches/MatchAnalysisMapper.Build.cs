using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Application.Matches;

internal static partial class MatchAnalysisMapper
{
    internal static MatchNeutrals ToNeutrals(IReadOnlyList<NeutralCraft> crafts, ReplayAnalysis replay, MatchDetails match,
        MatchPlayer? player, IReadOnlyDictionary<int, Item> items, NameLookup names, NeutralRules? rules)
    {
        var byKey = items.Values.Where(item => item.Details is not null).DistinctBy(item => item.Details!.Key)
            .ToDictionary(item => item.Details!.Key);
        Item? ItemOf(string? name) =>
            name is null ? null : byKey.GetValueOrDefault(name.StartsWith("item_", StringComparison.Ordinal) ? name[5..] : name);
        var heroes = match.Players.OrderByDescending(hero => ReferenceEquals(hero, player)).Select(hero => hero.HeroId).ToList();
        var evaluated = NeutralItemEvaluator.Evaluate(heroes, crafts, replay.NeutralProgress, rules, (int)match.Duration.TotalSeconds);
        return new MatchNeutrals(
            rules?.Tiers.Select(tier => new NeutralTierInfo(tier.Tier, tier.OpensAtSeconds, tier.CraftCost)).ToList() ?? [],
            evaluated.Select(hero => new HeroNeutralItems(
                hero.HeroId, names.Hero(hero.HeroId), player?.HeroId == hero.HeroId,
                hero.Crafts.Select(craft => new NeutralCraftInfo(craft.Craft.Seconds, craft.Craft.Tier,
                    ItemOf(craft.Craft.Artifact)?.Id, ItemOf(craft.Craft.Artifact)?.Name ?? craft.Craft.Artifact,
                    ItemOf(craft.Craft.Enhancement)?.Id,
                    craft.Craft.Enhancement is null ? null : ItemOf(craft.Craft.Enhancement)?.Name ?? craft.Craft.Enhancement,
                    craft.Craft.EnhancementLevel, craft.DelaySeconds)).ToList(),
                hero.TiersSkipped, hero.AverageDelaySeconds, hero.Madstones?.Acquired, hero.Madstones?.Found,
                hero.Madstones?.InHandAtEnd, hero.Madstones?.NeutralGold)).ToList());
    }

    // Null when the catalog has no details (an old saved copy with OpenDota down): without components there is no
    // assembly to redo.
    internal static PlayerItemBuild? ToItemBuild(
        IReadOnlyList<ItemPurchase> purchases, MatchPlayer player, IReadOnlyDictionary<int, Item> items,
        IReadOnlyDictionary<string, ItemDetails>? rules = null)
    {
        var byKey = items.Values
            .Where(item => item.Details is not null)
            .DistinctBy(item => item.Details!.Key)
            .ToDictionary(item => item.Details!.Key);
        if (byKey.Count == 0)
        {
            return null;
        }

        var build = ItemBuildEvaluator.Evaluate(
            purchases.Where(purchase => purchase.HeroId == player.HeroId),
            rules ?? byKey.ToDictionary(pair => pair.Key, pair => pair.Value.Details!));

        return new PlayerItemBuild(
            // An item from a newer patch than the catalog has no name to show: left out.
            StartingItems: build.StartingItems
                .Where(item => byKey.ContainsKey(item.ItemKey))
                .Select(item => new StartingItemCount(byKey[item.ItemKey].Id, byKey[item.ItemKey].Name, item.Count))
                .ToList(),
            Items: build.Items
                .Where(item => byKey.ContainsKey(item.ItemKey))
                .Select(item => new TimedItem(byKey[item.ItemKey].Id, byKey[item.ItemKey].Name, item.Seconds, item.Assembled))
                .ToList());
    }
}

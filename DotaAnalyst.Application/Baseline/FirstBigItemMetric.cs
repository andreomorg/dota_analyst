using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Baseline;

internal static class FirstBigItemMetric
{
    public static IReadOnlyList<HeroMetrics> AddTo(
        IReadOnlyList<HeroMetrics> metrics,
        IReadOnlyList<ItemPurchase>? purchases,
        IReadOnlyDictionary<int, Item> items,
        IReadOnlyDictionary<string, ItemDetails>? rules = null)
    {
        var byKey = rules ?? items.Values
            .Where(item => item.Details is not null)
            .DistinctBy(item => item.Details!.Key)
            .ToDictionary(item => item.Details!.Key, item => item.Details!);
        if (byKey.Count == 0 || purchases is null)
        {
            return rules is null ? metrics : metrics.Select(hero => hero with { FirstBigItemSeconds = null }).ToList();
        }

        return metrics.Select(hero => hero with
        {
            FirstBigItemSeconds = ItemBuildEvaluator.Evaluate(
                purchases.Where(purchase => purchase.HeroId == hero.HeroId), byKey).FirstBigItemSeconds,
        }).ToList();
    }
}

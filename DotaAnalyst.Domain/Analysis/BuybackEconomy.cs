using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

public sealed record ObservedBuyback(double Seconds, int? RecordedCost);
public sealed record BuybackWindow(bool Available, IReadOnlyList<ObservedBuyback> Uses, long? RecordedCostTotal);

public static class BuybackEconomy
{
    public static BuybackWindow Window(int heroId, double from, double to, IReadOnlyList<BuybackUse>? uses)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        if (!double.IsFinite(from) || from < 0 || !double.IsFinite(to) || to < from) throw new ArgumentOutOfRangeException(nameof(from));
        if (uses is null) return new(false, [], null);
        var selected = uses.Where(use => use.HeroId == heroId && use.Seconds >= from && use.Seconds <= to)
            .OrderBy(use => use.Seconds).Select(use => new ObservedBuyback(use.Seconds, use.Cost)).ToList();
        return new(true, selected, selected.Any(use => use.RecordedCost is null) ? null
            : selected.Aggregate(0L, (total, use) => checked(total + use.RecordedCost!.Value)));
    }
}

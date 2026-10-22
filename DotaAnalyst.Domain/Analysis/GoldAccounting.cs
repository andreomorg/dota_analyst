using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

public enum GoldResolution { Unavailable, IndividualEvents, TwoSecondBuckets }

// Positive and negative recorded changes, NOT gross income/spending, balance, net worth or opportunity cost.
// Buckets have already netted changes within each raw reason and two-second interval.
public sealed record GoldChanges(long PositiveChanges, long NegativeChanges, long NetChange);

// RawReason is retained only for individual events. Null with bucket resolution means the mapping grouped it away.
public sealed record GoldSourceChanges(EconomySource Source, uint? RawReason, GoldChanges Changes);

public sealed record GoldWindow(
    double From, double To, GoldResolution Resolution, GoldChanges? Changes, IReadOnlyList<GoldSourceChanges> Sources);

public static class GoldAccounting
{
    // Individual events use the closed window [from, to]. Legacy buckets must fit wholly inside it. A present empty
    // event family is authoritative: do not fall back or add buckets and count the same observation twice.
    public static GoldWindow Window(int heroId, double from, double to,
        IReadOnlyList<GoldEvent>? events, IReadOnlyList<EconomyBucket>? buckets)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        if (!double.IsFinite(from) || from < 0) throw new ArgumentOutOfRangeException(nameof(from));
        if (!double.IsFinite(to) || to < from) throw new ArgumentOutOfRangeException(nameof(to));

        if (events is not null)
        {
            var selected = events.Where(entry => entry.HeroId == heroId && entry.Seconds >= from && entry.Seconds <= to)
                .Select(entry => (Source: SourceOf(entry.Reason), Reason: entry.Reason, entry.Amount));
            return Summarize(GoldResolution.IndividualEvents, selected);
        }
        if (buckets is not null)
        {
            var selected = buckets.Where(bucket => bucket.HeroId == heroId && bucket.Resource == EconomyResource.Gold
                    && bucket.Seconds >= from && (double)bucket.Seconds + EconomyBucket.Length <= to)
                .Select(bucket => (bucket.Source, Reason: (uint?)null, Amount: (long)bucket.Amount));
            return Summarize(GoldResolution.TwoSecondBuckets, selected);
        }
        return new(from, to, GoldResolution.Unavailable, null, []);

        GoldWindow Summarize(GoldResolution resolution, IEnumerable<(EconomySource Source, uint? Reason, long Amount)> rows)
        {
            var sources = rows.GroupBy(row => (row.Source, row.Reason))
                .Select(group => new GoldSourceChanges(group.Key.Source, group.Key.Reason, Sum(group.Select(row => row.Amount))))
                .OrderBy(row => row.Source).ThenBy(row => row.RawReason).ToList();
            // Sum the already separated sides; never net opposite signs across sources before reporting them.
            var positive = sources.Aggregate(0L, (sum, row) => checked(sum + row.Changes.PositiveChanges));
            var negative = sources.Aggregate(0L, (sum, row) => checked(sum + row.Changes.NegativeChanges));
            return new(from, to, resolution, new(positive, negative, checked(positive - negative)), sources);
        }
    }

    private static EconomySource SourceOf(uint? reason) => reason is <= int.MaxValue
        ? EconomyBucket.SourceOf(EconomyResource.Gold, (int)reason.Value) : EconomySource.Other;

    private static GoldChanges Sum(IEnumerable<long> amounts)
    {
        long positive = 0, negative = 0;
        foreach (var amount in amounts)
        {
            if (amount >= 0) positive = checked(positive + amount);
            else negative = checked(negative - amount);
        }
        return new(positive, negative, checked(positive - negative));
    }
}

namespace DotaAnalyst.Domain.Stats;

// One hero's statistics over one match, one value per point (Seconds: each minute, then the end): what the
// comparison base keeps of each hero, so it can compare at any moment.
public sealed record HeroStatHistory
{
    public HeroStatHistory(IReadOnlyList<int> seconds, IReadOnlyDictionary<StatKey, IReadOnlyList<int>> series)
    {
        ArgumentNullException.ThrowIfNull(seconds);
        ArgumentNullException.ThrowIfNull(series);
        if (seconds.Count == 0 || seconds.Zip(seconds.Skip(1)).Any(pair => pair.Second <= pair.First))
        {
            throw new ArgumentException("The points must go forward in time.", nameof(seconds));
        }

        if (series.Values.Any(values => values is null || values.Count != seconds.Count))
        {
            throw new ArgumentException("Every statistic needs one value per point.", nameof(series));
        }

        Seconds = seconds.ToList();
        Series = series.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<int>)entry.Value.ToList());
    }

    public IReadOnlyList<int> Seconds { get; }

    public IReadOnlyDictionary<StatKey, IReadOnlyList<int>> Series { get; }

    // The value at a moment of the match (the last point at or before it). Null after the end of the match — a match
    // that ended at 25:00 says nothing about 30:00 — and when the hero doesn't have the statistic.
    public int? ValueAt(StatKey key, int second)
    {
        if (second > Seconds[^1] || !Series.TryGetValue(key, out var values))
        {
            return null;
        }

        var index = 0;
        for (var i = 0; i < Seconds.Count && Seconds[i] <= second; i++)
        {
            index = i;
        }

        return values[index];
    }
}

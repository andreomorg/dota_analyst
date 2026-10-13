using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Domain.Replays;

// Value object: how the heroes' net worth and experience changed during the match, read from the replay.
// Point i of every hero is taken at Seconds[i] of the game clock: one point per minute (0:00, 1:00, ...) and a
// last one at the end of the match.
public record MatchTimeline
{
    public MatchTimeline(IReadOnlyList<int> seconds, IReadOnlyList<HeroTimeline> heroes)
    {
        ArgumentNullException.ThrowIfNull(seconds);
        ArgumentNullException.ThrowIfNull(heroes);
        if (seconds.Count == 0 || seconds[0] < 0 || seconds.Zip(seconds.Skip(1)).Any(pair => pair.Second <= pair.First))
        {
            throw new ArgumentException("The points must start at 0:00 or later and go forward in time.", nameof(seconds));
        }

        if (heroes.Any(hero => hero is null || hero.NetWorth.Count != seconds.Count))
        {
            throw new ArgumentException("Every hero needs one value per point.", nameof(heroes));
        }

        // Heroes are matched by hero ID, as in ReplayAnalysis.
        if (heroes.DistinctBy(hero => hero.HeroId).Count() != heroes.Count)
        {
            throw new ArgumentException("Each hero can appear only once in a match.", nameof(heroes));
        }

        Seconds = seconds.ToList();
        Heroes = heroes.ToList();
    }

    public IReadOnlyList<int> Seconds { get; }

    public IReadOnlyList<HeroTimeline> Heroes { get; }

    public HeroTimeline? FindHero(int heroId) => Heroes.FirstOrDefault(hero => hero.HeroId == heroId);
}

// One hero's values at each point of the MatchTimeline: net worth and experience (every analysis with a timeline) and
// the other statistics saved per minute (StatCatalog.SavedPerMinute; analyses saved before them only have the two).
public record HeroTimeline
{
    public HeroTimeline(
        int heroId, Team team, IReadOnlyList<int> netWorth, IReadOnlyList<int> experience,
        IReadOnlyDictionary<StatKey, IReadOnlyList<int>>? otherSeries = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentNullException.ThrowIfNull(netWorth);
        ArgumentNullException.ThrowIfNull(experience);
        var series = new Dictionary<StatKey, IReadOnlyList<int>>
        {
            [StatKey.NetWorth] = netWorth.ToList(),
            [StatKey.Experience] = experience.ToList(),
        };
        foreach (var (key, values) in otherSeries ?? new Dictionary<StatKey, IReadOnlyList<int>>())
        {
            if (key is StatKey.NetWorth or StatKey.Experience || StatCatalog.Get(key).Source != StatSource.ReplayMinutes)
            {
                throw new ArgumentException($"{key} is not one of the other statistics saved per minute.", nameof(otherSeries));
            }

            series[key] = values?.ToList() ?? throw new ArgumentException($"{key} has no values.", nameof(otherSeries));
        }

        if (series.Values.Any(values => values.Count != netWorth.Count))
        {
            throw new ArgumentException("Every statistic needs the same points.", nameof(otherSeries));
        }

        if (series.Values.Any(values => values.Any(value => value < 0)))
        {
            throw new ArgumentException("The statistics can't be negative.", nameof(netWorth));
        }

        HeroId = heroId;
        Team = team;
        Series = series;
    }

    public int HeroId { get; }

    public Team Team { get; }

    // Every statistic this hero has, one value per point of the timeline.
    public IReadOnlyDictionary<StatKey, IReadOnlyList<int>> Series { get; }

    public IReadOnlyList<int> NetWorth => Series[StatKey.NetWorth];

    public IReadOnlyList<int> Experience => Series[StatKey.Experience];

    // Saved before every statistic of the catalog was read per minute.
    public bool HasEverySavedStat => StatCatalog.SavedPerMinute.All(Series.ContainsKey);

    // Records compare their properties by reference; two timelines with the same values are the same.
    public virtual bool Equals(HeroTimeline? other) =>
        other is not null && HeroId == other.HeroId && Team == other.Team && Series.Count == other.Series.Count
        && Series.All(entry => other.Series.TryGetValue(entry.Key, out var values) && values.SequenceEqual(entry.Value));

    public override int GetHashCode() => HashCode.Combine(HeroId, Team, Series.Count);
}

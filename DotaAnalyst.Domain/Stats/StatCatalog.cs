namespace DotaAnalyst.Domain.Stats;

// Every statistic the analysis knows, as one value per minute of the match (MatchStatTimeline). This catalog is the
// one place that says what each statistic is: where it comes from, whether it adds up or is a snapshot, its unit and
// whether more or less is better. The API, the comparisons and the React page read it; none of them defines a
// statistic of its own. The Portuguese names live in the React page (src/stats/statLabels.js), one per key here.
public enum StatKey
{
    Gold,
    Experience,
    NetWorth,
    LastHits,
    Denies,
    Level,
    Kills,
    Deaths,
    Assists,
    HeroDamage,
    DamageFromHeroes,
    DamageTaken,
    Healing,
    ObserversPlaced,
    SentriesPlaced,
    EnemyWardsDestroyed,
}

// Where the values come from.
public enum StatSource
{
    // A list per minute read from the replay and saved with the analysis (HeroTimeline.Series).
    ReplayMinutes,

    // Counted when the analysis is read, from events saved with their time (wards, deaths...).
    ReplayEvents,
}

public enum StatKind
{
    // Adds up during the match (last hits so far): what happened between two moments is the difference.
    Total,

    // A snapshot of the moment (net worth, level): the difference between two moments is a gain, not a count.
    State,
}

public enum StatUnit
{
    Count,
    Gold,
    Experience,
    Damage,
    Healing,
    Level,
}

public enum BetterWhen
{
    Higher,
    Lower,

    // Depends on the hero and the plan (damage taken: a tank should take it).
    Neither,
}

public sealed record StatDefinition(StatKey Key, StatSource Source, StatKind Kind, StatUnit Unit, BetterWhen Better);

public static class StatCatalog
{
    public static IReadOnlyList<StatDefinition> All { get; } =
    [
        // timelines.perMinute of the replay: the game's own counters, read when each minute turns.
        new(StatKey.Gold, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Gold, BetterWhen.Higher),
        new(StatKey.Experience, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Experience, BetterWhen.Higher),
        new(StatKey.NetWorth, StatSource.ReplayMinutes, StatKind.State, StatUnit.Gold, BetterWhen.Higher),
        new(StatKey.LastHits, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Count, BetterWhen.Higher),
        new(StatKey.Denies, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Count, BetterWhen.Higher),
        new(StatKey.Level, StatSource.ReplayMinutes, StatKind.State, StatUnit.Level, BetterWhen.Higher),
        new(StatKey.Kills, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Count, BetterWhen.Higher),
        new(StatKey.Deaths, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Count, BetterWhen.Lower),
        new(StatKey.Assists, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Count, BetterWhen.Higher),
        new(StatKey.HeroDamage, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Damage, BetterWhen.Higher),

        // combat.perMinute of the replay (the damage of each minute, added up here).
        new(StatKey.DamageFromHeroes, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Damage, BetterWhen.Neither),
        new(StatKey.DamageTaken, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Damage, BetterWhen.Neither),
        new(StatKey.Healing, StatSource.ReplayMinutes, StatKind.Total, StatUnit.Healing, BetterWhen.Higher),

        // vision.wards of the replay.
        new(StatKey.ObserversPlaced, StatSource.ReplayEvents, StatKind.Total, StatUnit.Count, BetterWhen.Higher),
        new(StatKey.SentriesPlaced, StatSource.ReplayEvents, StatKind.Total, StatUnit.Count, BetterWhen.Higher),
        new(StatKey.EnemyWardsDestroyed, StatSource.ReplayEvents, StatKind.Total, StatUnit.Count, BetterWhen.Higher),
    ];

    // The statistics saved per minute with the analysis: an analysis without one of them is read again.
    public static IReadOnlyList<StatKey> SavedPerMinute { get; } =
        All.Where(stat => stat.Source == StatSource.ReplayMinutes).Select(stat => stat.Key).ToList();

    public static StatDefinition Get(StatKey key) => All.First(stat => stat.Key == key);
}

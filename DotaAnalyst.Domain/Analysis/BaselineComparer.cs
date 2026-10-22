using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Domain.Analysis;

// Where the comparison base comes from.
public enum BaselineSource
{
    // Professional league matches (OpenDota's pro matches; roles inferred).
    Pro,
    // Public ranked matches of the highest ranks OpenDota lists.
    HighRank,
}

// The numbers compared with the base. Counts are per 30 minutes of game, so matches of different lengths compare.
public enum ComparedMetric
{
    DeathsPer30,
    IsolatedDeathsPer30,
    EnemyJungleDeathsPer30,
    AloneOnEnemySide,
    NetWorthAt10,
    LastHitsAt10,
    NetWorthAt20,
    ObserversPer30,
    SentriesPer30,
    ObserverLifetime,
    EnemyWardsDestroyedPer30,
    FightPresence,
    KillParticipation,
    QuickRedeathsPer30,
    LaneDamageDealt,
    LaneDamageTaken,
    TeamObserverUptime,
    LeadLostPerDeath,
    FirstBigItemSeconds,
    // Deaths by terrain (TerrainMetrics, MP8), per 30 minutes.
    DeathsOnEnemyHighGroundPer30,
    DeathsBelowEnemyHighGroundPer30,
    NarrowGroundDeathsPer30,
    DeepEnemyJungleDeathsPer30,
    // Vision (VisionMetrics): share of the time seen by the enemy, deaths per 30 minutes while tracked / unseen
    // before / to a killer from the fog, and kills from the fog per 30 minutes.
    SeenByEnemies,
    DeathsWhileTrackedPer30,
    DeathsUnseenBeforePer30,
    DeathsToKillerFromFogPer30,
    KillsFromFogPer30,
    // Of the ground in range of the hero's observer wards, the part they really saw (terrain, trees).
    ObserverSeenShare,
    // The map's objectives (MapObjectiveMetrics): the hero's counters per 30 minutes, and the share of the team's
    // Roshan / Tormentor kills and enemy towers it was there for.
    BountyRunesPer30,
    PowerRunesPer30,
    WisdomShrinesPer30,
    LotusesPer30,
    WatchersPer30,
    OutpostsPer30,
    CampsStackedPer30,
    RoshanPresence,
    TormentorPresence,
    TowerPresence,
    // Wisdom Shrines per 30 minutes: of the team's side lost to the enemy, of the enemy's side taken.
    OwnShrinesLostPer30,
    EnemyShrinesTakenPer30,
    // Teleports (TeleportMetrics) that arrived into a fight, per 30 minutes.
    TeleportsIntoFightsPer30,
    // Play quality (PlayQualityMetrics): share of the listed skillshots that hit, allies saved per 30 minutes.
    SkillshotAccuracy,
    SavesPer30,
}

// One hero's metrics in one match of the base, with the match's length (for the per-30-minutes numbers), and the
// hero's statistics minute by minute (null for matches collected before they were kept).
public record BaselineSample(HeroMetrics Metrics, int MatchSeconds, int? OpponentHeroId = null, HeroStatHistory? History = null);

// A statistic of the catalog at a moment of the match (StatKey at Second) in the base: the values at percentiles
// 0, 5 ... 100, like MetricDistribution.
public record TimedDistribution(StatKey Stat, int Second, IReadOnlyList<double> Quantiles, int SampleSize)
{
    public virtual bool Equals(TimedDistribution? other) =>
        other is not null && Stat == other.Stat && Second == other.Second && SampleSize == other.SampleSize
        && Quantiles.SequenceEqual(other.Quantiles);

    public override int GetHashCode() => HashCode.Combine(Stat, Second, SampleSize);
}

// How the player did on a statistic at a moment against the base.
public record TimedComparison(StatKey Stat, int Second, double Value, double Median, double BetterThan, int SampleSize);

// A metric's distribution in the base: the values at percentiles 0, 5, 10 ... 100 (QuantileCount points) and how
// many matches it came from. What is published to production: small however big the base is.
public record MetricDistribution(ComparedMetric Metric, IReadOnlyList<double> Quantiles, int SampleSize)
{
    public virtual bool Equals(MetricDistribution? other) =>
        other is not null && Metric == other.Metric && SampleSize == other.SampleSize && Quantiles.SequenceEqual(other.Quantiles);

    public override int GetHashCode() => HashCode.Combine(Metric, SampleSize);
}

// How the player did on one metric against the base: the median of the base and the share (0..1) of the base the
// player did better than.
public record MetricComparison(ComparedMetric Metric, double Value, double Median, double BetterThan, int SampleSize);

// Domain service: the base's distributions (made where the base is, on the project owner's machine) and the player's
// metrics against them (wherever the distributions are read: locally or in production).
public static class BaselineComparer
{
    // Fewer matches than this say too little: no distribution.
    public const int MinSampleSize = 10;

    // Percentiles 0, 5, 10 ... 100.
    public const int QuantileCount = 21;

    private const double ThirtyMinutes = 1800;

    // Each metric: how to read it (null when the hero doesn't have it) and whether higher is better.
    private static readonly (ComparedMetric Metric, Func<HeroMetrics, int, double?> Read, bool HigherIsBetter)[] Metrics =
    [
        (ComparedMetric.DeathsPer30, (hero, seconds) => Per30(hero.Deaths, seconds), false),
        (ComparedMetric.IsolatedDeathsPer30, (hero, seconds) => Per30(hero.IsolatedDeaths, seconds), false),
        (ComparedMetric.EnemyJungleDeathsPer30, (hero, seconds) => hero.DeathsByRegion is { } deaths ? Per30(deaths.EnemyJungle, seconds) : null, false),
        (ComparedMetric.AloneOnEnemySide, (hero, _) => hero.AloneOnEnemySideShare, false),
        (ComparedMetric.NetWorthAt10, (hero, _) => hero.NetWorthAt10, true),
        (ComparedMetric.LastHitsAt10, (hero, _) => hero.LastHitsAt10, true),
        (ComparedMetric.NetWorthAt20, (hero, _) => hero.NetWorthAt20, true),
        (ComparedMetric.ObserversPer30, (hero, seconds) => Per30(hero.ObserversPlaced, seconds), true),
        (ComparedMetric.SentriesPer30, (hero, seconds) => Per30(hero.SentriesPlaced, seconds), true),
        (ComparedMetric.ObserverLifetime, (hero, _) => hero.ObserverAverageLifetimeSeconds, true),
        (ComparedMetric.EnemyWardsDestroyedPer30, (hero, seconds) => Per30(hero.EnemyWardsDestroyed, seconds), true),
        (ComparedMetric.FightPresence, (hero, _) => hero is { TeamFights: > 0 and var fights, FightsNearby: { } near } ? (double)near / fights : null, true),
        (ComparedMetric.KillParticipation, (hero, _) => hero.KillParticipation, true),
        (ComparedMetric.QuickRedeathsPer30, (hero, seconds) => hero.QuickRedeaths is { } quick ? Per30(quick, seconds) : null, false),
        (ComparedMetric.LaneDamageDealt, (hero, _) => hero.LaneDamageDealt, true),
        (ComparedMetric.LaneDamageTaken, (hero, _) => hero.LaneDamageTaken, false),
        (ComparedMetric.TeamObserverUptime, (hero, _) => hero.TeamObserverUptime, true),
        (ComparedMetric.LeadLostPerDeath, (hero, _) => hero.LeadLostPerDeath, false),
        (ComparedMetric.FirstBigItemSeconds, (hero, _) => hero.FirstBigItemSeconds, false),
        (ComparedMetric.DeathsOnEnemyHighGroundPer30, (hero, seconds) => hero.Terrain is { } terrain ? Per30(terrain.DeathsOnEnemyHighGround, seconds) : null, false),
        (ComparedMetric.DeathsBelowEnemyHighGroundPer30, (hero, seconds) => hero.Terrain is { } terrain ? Per30(terrain.DeathsBelowEnemyHighGround, seconds) : null, false),
        (ComparedMetric.NarrowGroundDeathsPer30, (hero, seconds) => hero.Terrain?.DeathsInNarrowGround is { } narrow ? Per30(narrow, seconds) : null, false),
        (ComparedMetric.DeepEnemyJungleDeathsPer30, (hero, seconds) => hero.Terrain?.DeathsInDeepEnemyJungle is { } deep ? Per30(deep, seconds) : null, false),
        // Less seen is better: the enemy knows less where the hero is.
        (ComparedMetric.SeenByEnemies, (hero, _) => hero.Vision?.SeenByEnemiesShare, false),
        (ComparedMetric.DeathsWhileTrackedPer30, (hero, seconds) => hero.Vision?.DeathsWhileTracked is { } tracked ? Per30(tracked, seconds) : null, false),
        (ComparedMetric.DeathsUnseenBeforePer30, (hero, seconds) => hero.Vision?.DeathsUnseenBefore is { } unseen ? Per30(unseen, seconds) : null, false),
        (ComparedMetric.DeathsToKillerFromFogPer30, (hero, seconds) => hero.Vision?.DeathsToKillerFromFog is { } fogDeaths ? Per30(fogDeaths, seconds) : null, false),
        (ComparedMetric.KillsFromFogPer30, (hero, seconds) => hero.Vision?.KillsFromFog is { } fogKills ? Per30(fogKills, seconds) : null, true),
        (ComparedMetric.ObserverSeenShare, (hero, _) => hero.Vision?.ObserverSeenShare, true),
        (ComparedMetric.BountyRunesPer30, (hero, seconds) => hero.Objectives?.Counts is { } counts ? Per30(counts.BountyRunes, seconds) : null, true),
        (ComparedMetric.PowerRunesPer30, (hero, seconds) => hero.Objectives?.Counts is { } counts ? Per30(counts.PowerRunes, seconds) : null, true),
        (ComparedMetric.WisdomShrinesPer30, (hero, seconds) => hero.Objectives?.Counts is { } counts ? Per30(counts.WisdomShrines, seconds) : null, true),
        (ComparedMetric.LotusesPer30, (hero, seconds) => hero.Objectives?.Counts is { } counts ? Per30(counts.Lotuses, seconds) : null, true),
        (ComparedMetric.WatchersPer30, (hero, seconds) => hero.Objectives?.Counts is { } counts ? Per30(counts.Watchers, seconds) : null, true),
        (ComparedMetric.OutpostsPer30, (hero, seconds) => hero.Objectives?.Counts is { } counts ? Per30(counts.Outposts, seconds) : null, true),
        (ComparedMetric.CampsStackedPer30, (hero, seconds) => hero.Objectives?.Counts is { } counts ? Per30(counts.CampsStacked, seconds) : null, true),
        (ComparedMetric.RoshanPresence, (hero, _) => hero.Objectives is { RoshanKills: > 0 and var kills, RoshanKillsNearby: { } near } ? (double)near / kills : null, true),
        (ComparedMetric.TormentorPresence, (hero, _) => hero.Objectives is { TormentorKills: > 0 and var kills, TormentorKillsNearby: { } near } ? (double)near / kills : null, true),
        (ComparedMetric.TowerPresence, (hero, _) => hero.Objectives is { EnemyTowersTaken: > 0 and var towers, EnemyTowersTakenNearby: { } near } ? (double)near / towers : null, true),
        (ComparedMetric.OwnShrinesLostPer30, (hero, seconds) => hero.Objectives?.OwnShrinesLost is { } lost ? Per30(lost, seconds) : null, false),
        (ComparedMetric.EnemyShrinesTakenPer30, (hero, seconds) => hero.Objectives?.EnemyShrinesTaken is { } stolen ? Per30(stolen, seconds) : null, true),
        (ComparedMetric.TeleportsIntoFightsPer30, (hero, seconds) => hero.Teleports?.IntoFights is { } joined ? Per30(joined, seconds) : null, true),
        (ComparedMetric.SkillshotAccuracy, (hero, _) => hero.Plays is { SkillshotUses: > 0 and var uses, SkillshotHits: { } hits } ? (double)hits / uses : null, true),
        (ComparedMetric.SavesPer30, (hero, seconds) => hero.Plays?.Saves is { } saves ? Per30(saves, seconds) : null, true),
    ];

    // The distribution of each metric the base has at least MinSampleSize values of.
    public static IReadOnlyList<MetricDistribution> Distribute(IReadOnlyList<BaselineSample> samples)
    {
        var distributions = new List<MetricDistribution>();
        foreach (var (metric, read, _) in Metrics)
        {
            var values = samples.Select(sample => read(sample.Metrics, sample.MatchSeconds)).OfType<double>().Order().ToList();
            if (values.Count < MinSampleSize)
            {
                continue;
            }

            var quantiles = Enumerable.Range(0, QuantileCount)
                .Select(i => Percentile(values, (double)i / (QuantileCount - 1)))
                .ToList();
            distributions.Add(new MetricDistribution(metric, quantiles, values.Count));
        }

        return distributions;
    }

    // The player against each distribution the player has a value for.
    public static IReadOnlyList<MetricComparison> Compare(
        HeroMetrics player, int playerMatchSeconds, IReadOnlyList<MetricDistribution> distributions)
    {
        var comparisons = new List<MetricComparison>();
        foreach (var (metric, read, higherIsBetter) in Metrics)
        {
            if (read(player, playerMatchSeconds) is not { } value
                || distributions.FirstOrDefault(distribution => distribution.Metric == metric) is not { Quantiles.Count: QuantileCount } distribution)
            {
                continue;
            }

            var below = ShareBelow(distribution.Quantiles, value);
            comparisons.Add(new MetricComparison(
                metric, value, distribution.Quantiles[QuantileCount / 2], higherIsBetter ? below : 1 - below, distribution.SampleSize));
        }

        return comparisons;
    }

    // The moments the base compares the statistics of the catalog at (5, 10, 15, 20, 25, 30 and 40 minutes): enough for
    // the page's time pickers, few enough for the production mirror to stay small.
    public static IReadOnlyList<int> Checkpoints { get; } = [300, 600, 900, 1200, 1500, 1800, 2400];

    // Statistics where more or less is better (the others, like damage taken, have no "better than").
    private static IEnumerable<StatDefinition> ComparableStats => StatCatalog.All.Where(stat => stat.Better != BetterWhen.Neither);

    // Each comparable statistic at each checkpoint, from the matches that lasted until then (a match over at 25:00 says
    // nothing about 30:00), when at least MinSampleSize of them have it.
    public static IReadOnlyList<TimedDistribution> DistributeTimed(IReadOnlyList<HeroStatHistory> histories)
    {
        var distributions = new List<TimedDistribution>();
        foreach (var stat in ComparableStats)
        {
            foreach (var second in Checkpoints)
            {
                var values = histories
                    .Select(history => history.ValueAt(stat.Key, second))
                    .OfType<int>()
                    .Select(value => (double)value)
                    .Order()
                    .ToList();
                if (values.Count < MinSampleSize)
                {
                    continue;
                }

                var quantiles = Enumerable.Range(0, QuantileCount)
                    .Select(i => Percentile(values, (double)i / (QuantileCount - 1)))
                    .ToList();
                distributions.Add(new TimedDistribution(stat.Key, second, quantiles, values.Count));
            }
        }

        return distributions;
    }

    // The player against each statistic and moment of the base the player's match reached.
    public static IReadOnlyList<TimedComparison> CompareTimed(HeroStatHistory player, IReadOnlyList<TimedDistribution> distributions)
    {
        var comparisons = new List<TimedComparison>();
        foreach (var distribution in distributions)
        {
            var better = StatCatalog.Get(distribution.Stat).Better;
            if (better == BetterWhen.Neither || distribution.Quantiles.Count != QuantileCount
                || player.ValueAt(distribution.Stat, distribution.Second) is not { } value)
            {
                continue;
            }

            var below = ShareBelow(distribution.Quantiles, value);
            comparisons.Add(new TimedComparison(
                distribution.Stat, distribution.Second, value, distribution.Quantiles[QuantileCount / 2],
                better == BetterWhen.Higher ? below : 1 - below, distribution.SampleSize));
        }

        return comparisons;
    }

    // Where the value falls in the distribution (0..1), interpolating between the percentiles; on a run of equal
    // percentiles (many players with 0 deaths) the middle of the run, like a tie counting half.
    private static double ShareBelow(IReadOnlyList<double> quantiles, double value)
    {
        var last = quantiles.Count - 1;
        if (value < quantiles[0])
        {
            return 0;
        }

        if (value > quantiles[last])
        {
            return 1;
        }

        var first = Enumerable.Range(0, quantiles.Count).First(i => quantiles[i] >= value);
        if (quantiles[first] == value)
        {
            var end = Enumerable.Range(0, quantiles.Count).Last(i => quantiles[i] == value);
            return (first + end) / 2.0 / last;
        }

        var fraction = (value - quantiles[first - 1]) / (quantiles[first] - quantiles[first - 1]);
        return (first - 1 + fraction) / last;
    }

    // Linear interpolation between the sorted values (the usual "type 7" percentile).
    private static double Percentile(List<double> sorted, double p)
    {
        var position = p * (sorted.Count - 1);
        var low = (int)Math.Floor(position);
        var high = (int)Math.Ceiling(position);
        return sorted[low] + (sorted[high] - sorted[low]) * (position - low);
    }

    private static double? Per30(int count, int seconds) => seconds <= 0 ? null : count * ThirtyMinutes / seconds;
}

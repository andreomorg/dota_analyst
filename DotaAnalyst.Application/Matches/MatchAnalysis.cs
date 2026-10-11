using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Matches;

/// <summary>A whole match, analyzed from the point of view of one player.</summary>
/// <param name="MatchId">Dota match ID.</param>
/// <param name="DurationSeconds">Match duration, in seconds.</param>
/// <param name="StartTime">When the match started (UTC).</param>
/// <param name="RadiantWon">True when Radiant won, false when Dire won; null when the result is not known.</param>
/// <param name="RadiantScore">Radiant kills.</param>
/// <param name="DireScore">Dire kills.</param>
/// <param name="IsParsed">True when OpenDota parsed the replay (detailed data available).</param>
/// <param name="Radiant">The Radiant players.</param>
/// <param name="Dire">The Dire players.</param>
/// <param name="Performance">How the requested player did; null when they are not in the match (or have an anonymous profile).</param>
public record MatchAnalysis(
    long MatchId,
    int DurationSeconds,
    DateTimeOffset StartTime,
    bool? RadiantWon,
    int RadiantScore,
    int DireScore,
    bool IsParsed,
    IReadOnlyList<MatchPlayerSummary> Radiant,
    IReadOnlyList<MatchPlayerSummary> Dire,
    PlayerPerformance? Performance);

/// <summary>One of the ten players of the match.</summary>
/// <param name="AccountId">Dota account ID; null for players with an anonymous profile.</param>
/// <param name="IsCurrentPlayer">True for the player whose point of view the analysis uses.</param>
/// <param name="HeroId">Hero played, as OpenDota's hero ID.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="Kills">Kills.</param>
/// <param name="Deaths">Deaths.</param>
/// <param name="Assists">Assists.</param>
/// <param name="LastHits">Last hits (creeps killed).</param>
/// <param name="Denies">Own creeps denied.</param>
/// <param name="GoldPerMinute">Gold per minute.</param>
/// <param name="ExperiencePerMinute">Experience per minute.</param>
/// <param name="NetWorth">Net worth at the end of the match.</param>
/// <param name="HeroDamage">Damage dealt to heroes.</param>
/// <param name="TowerDamage">Damage dealt to buildings.</param>
/// <param name="HeroHealing">Healing done to heroes.</param>
/// <param name="Level">Hero level at the end of the match.</param>
/// <param name="Items">Inventory item names (empty slots left out; "Unknown item" when the catalog doesn't know one).</param>
/// <param name="NeutralItem">Neutral item name; null when there is none.</param>
public record MatchPlayerSummary(
    long? AccountId,
    bool IsCurrentPlayer,
    int HeroId,
    string HeroName,
    int Kills,
    int Deaths,
    int Assists,
    int LastHits,
    int Denies,
    int GoldPerMinute,
    int ExperiencePerMinute,
    int NetWorth,
    int HeroDamage,
    int TowerDamage,
    int HeroHealing,
    int Level,
    IReadOnlyList<string> Items,
    string? NeutralItem);

/// <summary>How the player did compared to other players of the same hero.</summary>
/// <param name="HeroId">Hero played.</param>
/// <param name="HeroName">Hero name.</param>
/// <param name="IsRadiant">True when the player was on Radiant.</param>
/// <param name="Won">Whether the player won; null when the result is not known.</param>
/// <param name="Metrics">The metrics OpenDota benchmarks for this player (hero healing left out when zero), with value, percentile and rating.</param>
/// <param name="Strengths">Metrics rated strong (percentile ≥ 0.70), best first.</param>
/// <param name="Weaknesses">Metrics to improve (percentile ≤ 0.30), worst first.</param>
public record PlayerPerformance(
    int HeroId,
    string HeroName,
    bool IsRadiant,
    bool? Won,
    IReadOnlyList<MetricPerformance> Metrics,
    IReadOnlyList<BenchmarkMetric> Strengths,
    IReadOnlyList<BenchmarkMetric> Weaknesses);

/// <summary>One performance metric of the player.</summary>
/// <param name="Metric">Which metric.</param>
/// <param name="Value">The player's value (per minute for the *PerMinute metrics).</param>
/// <param name="Percentile">From 0 to 1 against other players of the same hero; higher is always better, even for deaths.</param>
/// <param name="Rating">strong (≥ 0.70), average, or weak (≤ 0.30).</param>
public record MetricPerformance(BenchmarkMetric Metric, double Value, double Percentile, PerformanceRating Rating);

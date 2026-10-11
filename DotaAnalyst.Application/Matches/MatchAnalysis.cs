using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Application.Matches;

// What the API returns for GET /api/v1/players/{id}/matches/{matchId}.

public record MatchAnalysis(
    long MatchId,
    int DurationSeconds,
    DateTimeOffset StartTime,
    // Null when the match result is not known yet.
    bool? RadiantWon,
    int RadiantScore,
    int DireScore,
    bool IsParsed,
    IReadOnlyList<MatchPlayerSummary> Radiant,
    IReadOnlyList<MatchPlayerSummary> Dire,
    // Null when the requested player is not in the match (or has an anonymous profile).
    PlayerPerformance? Performance);

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

public record PlayerPerformance(
    int HeroId,
    string HeroName,
    bool IsRadiant,
    bool? Won,
    IReadOnlyList<MetricPerformance> Metrics,
    // Best first.
    IReadOnlyList<BenchmarkMetric> Strengths,
    // Worst first.
    IReadOnlyList<BenchmarkMetric> Weaknesses);

public record MetricPerformance(BenchmarkMetric Metric, double Value, double Percentile, PerformanceRating Rating);

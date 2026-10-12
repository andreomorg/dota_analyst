using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

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
/// <param name="ReplayStatus">Where our own reading of the replay stands (every match with a replay; notNeeded is no longer used). While processing, ask again in a few seconds.</param>
/// <param name="Laning">The player's laning phase, read from the replay; null unless replayStatus is ready and the player is in the match.</param>
/// <param name="Timeline">Net worth and experience during the whole match, read from the replay; null unless replayStatus is ready (and for replays read before the timeline existed).</param>
/// <param name="Deaths">Where and when the player died, read from the replay, in time order; null unless replayStatus is ready and the player is in the match.</param>
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
    PlayerPerformance? Performance,
    ReplayStatus ReplayStatus,
    LaningPhase? Laning,
    MatchTimelineChart? Timeline = null,
    IReadOnlyList<PlayerDeath>? Deaths = null);

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

/// <summary>How the player's lane went, at the end of the laning phase (10:00 of the game clock).</summary>
/// <param name="EndsAtSeconds">Game clock, in seconds, at which the numbers were taken: 600, or less when the match ended earlier.</param>
/// <param name="Lane">Where the player spent the laning phase: top, middle, bottom (physical lanes, the same for both teams), jungle or roaming.</param>
/// <param name="Outcome">won (lane net worth lead ≥ 1000), lost (≤ −1000) or even; null in the jungle, roaming, or when no enemy was in the lane.</param>
/// <param name="NetWorthDifference">Net worth of the player and allies minus the opponents'; null when outcome is null.</param>
/// <param name="Player">The player's numbers.</param>
/// <param name="Allies">Teammates in the same lane (without the player), richest first.</param>
/// <param name="Opponents">Enemies in the same lane, richest first.</param>
public record LaningPhase(
    int EndsAtSeconds,
    Lane Lane,
    LaneOutcome? Outcome,
    int? NetWorthDifference,
    LaningPlayer Player,
    IReadOnlyList<LaningPlayer> Allies,
    IReadOnlyList<LaningPlayer> Opponents);

/// <summary>One hero's numbers at the end of the laning phase.</summary>
/// <param name="HeroId">Hero played.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="LastHits">Last hits (creeps killed).</param>
/// <param name="Denies">Own creeps denied.</param>
/// <param name="NetWorth">Gold plus the value of the items.</param>
/// <param name="Experience">Experience points.</param>
/// <param name="Deaths">Deaths.</param>
public record LaningPlayer(
    int HeroId,
    string HeroName,
    int LastHits,
    int Denies,
    int NetWorth,
    int Experience,
    int Deaths);

/// <summary>Net worth and experience during the whole match, for the charts.</summary>
/// <param name="Seconds">Game clock, in seconds, of each point: one per minute (0, 60, 120…) and a last one at the end of the match. Every list below has one value per point.</param>
/// <param name="Perspective">The team the leads are counted for: the player's team, or radiant when the player is not in the match.</param>
/// <param name="NetWorthLead">Net worth of the perspective team minus the other team's, at each point (negative when behind).</param>
/// <param name="ExperienceLead">Experience of the perspective team minus the other team's, at each point.</param>
/// <param name="Lane">The player and the heroes of their lane (allies and opponents, as in the laning phase); just the player in the jungle or roaming; empty when the player is not in the match.</param>
public record MatchTimelineChart(
    IReadOnlyList<int> Seconds,
    Team Perspective,
    IReadOnlyList<int> NetWorthLead,
    IReadOnlyList<int> ExperienceLead,
    IReadOnlyList<TimelineHero> Lane);

/// <summary>One hero's net worth and experience at each point of the timeline.</summary>
/// <param name="HeroId">Hero played.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="Side">player (the point of view), ally or opponent.</param>
/// <param name="NetWorth">Net worth at each point.</param>
/// <param name="Experience">Experience at each point.</param>
public record TimelineHero(
    int HeroId,
    string HeroName,
    LaneSide Side,
    IReadOnlyList<int> NetWorth,
    IReadOnlyList<int> Experience);

/// <summary>Which side of the player's lane a hero was on.</summary>
public enum LaneSide
{
    Player,
    Ally,
    Opponent,
}

/// <summary>One death of the player, read from the replay.</summary>
/// <param name="Seconds">Game clock of the death, in seconds (negative before the horn).</param>
/// <param name="X">Map coordinate: roughly −8200 (Radiant's side, left) to 8200.</param>
/// <param name="Y">Map coordinate: roughly −8200 (Radiant's side, bottom) to 8200.</param>
/// <param name="Killer">What dealt the killing blow: hero, tower, creeps, neutrals, roshan or other.</param>
/// <param name="KillerHeroId">The hero that got the kill; null unless killer is hero (and for hero kills the replay does not attribute).</param>
/// <param name="KillerHeroName">That hero's name, or "Unknown hero"; null when killerHeroId is null.</param>
/// <param name="Reincarnated">The hero came back (Aegis, Wraith King): not a death on the scoreboard.</param>
/// <param name="InLaningPhase">Died before 10:00 of the game clock.</param>
public record PlayerDeath(
    int Seconds,
    int X,
    int Y,
    KillerKind Killer,
    int? KillerHeroId,
    string? KillerHeroName,
    bool Reincarnated,
    bool InLaningPhase);
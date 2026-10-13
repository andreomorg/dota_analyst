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
/// <param name="Wards">Every ward of both teams, read from the replay, in the order they were placed; null unless replayStatus is ready.</param>
/// <param name="Damage">The player's damage to and from enemy heroes and by ability/item, read from the replay; null unless replayStatus is ready and the player is in the match.</param>
/// <param name="Fights">The teamfights of the match (3+ deaths at most 15 s apart), in time order, with the player's part; null unless replayStatus is ready.</param>
/// <param name="Objectives">Towers, barracks and Ancients that fell and Roshan/Tormentor kills of both teams, read from the replay, in time order (the buildings of the base besides towers 4 left out); null unless replayStatus is ready.</param>
/// <param name="SmartStats">Numbers that show where the player could improve (positioning, economy, vision), compared with the enemy of the same role and the match average, and what to improve; null unless replayStatus is ready and the player is in the match, and for matches without role queue (Turbo, unranked).</param>
/// <param name="ItemBuild">The player's starting items and when each item of the build was ready (approximate: items sold or given away still count); null unless replayStatus is ready and the player is in the match, or when the item catalog is unavailable.</param>
/// <param name="Heatmaps">Where each of the ten heroes spent its time alive, per game phase, read from the replay; null unless replayStatus is ready (and for replays read before heatmaps existed).</param>
/// <param name="AbilityBuild">The player's ability level-ups in replay order; null unless replayStatus is ready and the player is in the match.</param>
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
    IReadOnlyList<PlayerDeath>? Deaths = null,
    IReadOnlyList<MapWard>? Wards = null,
    PlayerDamage? Damage = null,
    IReadOnlyList<FightSummary>? Fights = null,
    PlayerItemBuild? ItemBuild = null,
    IReadOnlyList<MapObjectiveSummary>? Objectives = null,
    SmartStats? SmartStats = null,
    HeatmapSet? Heatmaps = null,
    PlayerAbilityBuild? AbilityBuild = null);

/// <summary>The ability level-ups of the player whose point of view the analysis uses.</summary>
/// <param name="Upgrades">Ability names and levels in the order recorded by the replay.</param>
public record PlayerAbilityBuild(IReadOnlyList<AbilityBuildUpgrade> Upgrades);

/// <summary>One ability level-up in the replay.</summary>
/// <param name="Seconds">Game-clock time in seconds.</param>
/// <param name="AbilityName">The parser's internal ability name.</param>
/// <param name="Level">The resulting level of this ability.</param>
public record AbilityBuildUpgrade(int Seconds, string AbilityName, int Level);

/// <summary>Where each hero spent its time alive, as a grid over the map (column 0 = west edge, row 0 = north edge), per game phase.</summary>
/// <param name="GridSize">Columns and rows of the grid.</param>
/// <param name="MapMin">Map coordinate of the grid's west and south edges (the same square as the game's minimap image).</param>
/// <param name="MapMax">Map coordinate of the grid's east and north edges.</param>
/// <param name="SecondsPerSample">About how many seconds of game each sample stands for.</param>
/// <param name="Heroes">One heatmap per hero, Radiant first.</param>
/// <param name="MatchAverage">The average of the ten heroes' indicators, per phase.</param>
public record HeatmapSet(
    int GridSize, int MapMin, int MapMax, int SecondsPerSample, IReadOnlyList<HeroHeatmapView> Heroes,
    PhaseIndicators? MatchAverage = null);

/// <summary>Where and how a hero played, from its heatmap: the whole match and each phase.</summary>
/// <param name="All">The whole match; null when the hero has no samples.</param>
/// <param name="EarlyGame">0-10 minutes; null without samples there.</param>
/// <param name="MidGame">10-20 minutes; null without samples there.</param>
/// <param name="LateGame">From 20 minutes on; null without samples there.</param>
public record PhaseIndicators(
    PositionIndicatorsView? All, PositionIndicatorsView? EarlyGame, PositionIndicatorsView? MidGame, PositionIndicatorsView? LateGame);

/// <summary>Position indicators of one hero in one period.</summary>
/// <param name="AliveSeconds">Time alive with a known position, in seconds.</param>
/// <param name="Advance">How far forward on average, from the own fountain (0) to the enemy's (1); the river is about 0.5.</param>
/// <param name="Coverage">Share of the playable map the hero stood in (cells of about 590 units, at least 4 s in each).</param>
/// <param name="Concentration">Share of the time in the 5 most visited cells.</param>
/// <param name="EnemySide">Share of the time past the river, on the enemy's side.</param>
/// <param name="NearAlly">Share of the time with at least one ally hero within 1500 units; null for older analyses.</param>
/// <param name="Grouped">Share of the time with at least two ally heroes within 1500 units; null for older analyses.</param>
public record PositionIndicatorsView(
    int AliveSeconds, double Advance, double Coverage, double Concentration, double EnemySide, double? NearAlly, double? Grouped);

/// <summary>The heatmap of one hero. Each phase is a flat list of [column, row, samples] triples, only for the cells the hero was in.</summary>
/// <param name="HeroId">Hero, as OpenDota's hero ID.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="Team">The hero's team.</param>
/// <param name="IsCurrentPlayer">True for the hero of the player whose point of view the analysis uses.</param>
/// <param name="EarlyGame">0-10 minutes.</param>
/// <param name="MidGame">10-20 minutes.</param>
/// <param name="LateGame">From 20 minutes on.</param>
/// <param name="Indicators">Where and how the hero played (advance, coverage, allies close by…), per phase.</param>
public record HeroHeatmapView(
    int HeroId,
    string HeroName,
    Team Team,
    bool IsCurrentPlayer,
    IReadOnlyList<int> EarlyGame,
    IReadOnlyList<int> MidGame,
    IReadOnlyList<int> LateGame,
    PhaseIndicators? Indicators = null);

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
/// <param name="ItemIds">Inventory item IDs in the same order as Items.</param>
/// <param name="NeutralItemId">Neutral item ID; null when there is none.</param>
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
    string? NeutralItem,
    IReadOnlyList<int>? ItemIds = null,
    int? NeutralItemId = null);

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

/// <summary>One ward placed in the match, read from the replay.</summary>
/// <param name="Kind">observer or sentry.</param>
/// <param name="Team">The team that owns it.</param>
/// <param name="IsPlayersTeam">True when it belongs to the player's team; null when the player is not in the match.</param>
/// <param name="PlacedByHeroName">Hero that placed it, or "Unknown hero"; null when the replay does not say.</param>
/// <param name="X">Map coordinate, as in PlayerDeath.</param>
/// <param name="Y">Map coordinate, as in PlayerDeath.</param>
/// <param name="PlacedAt">Game clock when it was placed, in seconds.</param>
/// <param name="RemovedAt">Game clock when it was gone (expired or destroyed); null when it still existed when the match ended.</param>
/// <param name="Destroyed">Killed by a unit; false when it expired (or still existed at the end).</param>
/// <param name="DestroyedByHeroName">Hero that destroyed it; null when it expired or a non-hero unit killed it.</param>
/// <param name="InLaningPhase">Placed before 10:00 of the game clock.</param>
public record MapWard(
    WardKind Kind,
    Team Team,
    bool? IsPlayersTeam,
    string? PlacedByHeroName,
    int X,
    int Y,
    int PlacedAt,
    int? RemovedAt,
    bool Destroyed,
    string? DestroyedByHeroName,
    bool InLaningPhase);

/// <summary>The player's damage over the whole match (illusions and summons count for their owner).</summary>
/// <param name="ToHeroes">Damage dealt to each enemy hero, highest first.</param>
/// <param name="FromHeroes">Damage taken from each enemy hero, highest first.</param>
/// <param name="ByInflictor">Damage dealt to enemy heroes by each ability or item, highest first; inflictor null = basic attacks.</param>
public record PlayerDamage(
    IReadOnlyList<HeroAmount> ToHeroes,
    IReadOnlyList<HeroAmount> FromHeroes,
    IReadOnlyList<InflictorAmount> ByInflictor);

/// <summary>An amount for one hero.</summary>
/// <param name="HeroId">Hero.</param>
/// <param name="HeroName">Hero name, or "Unknown hero".</param>
/// <param name="Amount">The amount.</param>
public record HeroAmount(int HeroId, string HeroName, int Amount);

/// <summary>An amount for one ability or item.</summary>
/// <param name="Inflictor">Internal name of the ability or item (lich_frost_nova, item_dagon_5); null for basic attacks.</param>
/// <param name="Amount">The amount.</param>
/// <param name="ItemId">The item's ID when the inflictor is an item the catalog knows (for its icon); null otherwise.</param>
/// <param name="ItemName">The item's name in the catalog; null when it isn't an item the catalog knows.</param>
public record InflictorAmount(string? Inflictor, int Amount, int? ItemId = null, string? ItemName = null);

/// <summary>One teamfight: hero deaths at most 15 s apart, at least 3 of them.</summary>
/// <param name="StartSeconds">Game clock of the first death.</param>
/// <param name="EndSeconds">Game clock of the last death.</param>
/// <param name="RadiantDeaths">Radiant heroes that died (Aegis deaths included).</param>
/// <param name="DireDeaths">Dire heroes that died.</param>
/// <param name="Winner">The team with fewer deaths; null on a tie.</param>
/// <param name="PlayersTeamWon">Whether the player's team won it; null on a tie or when the player is not in the match.</param>
/// <param name="Died">The player died in it.</param>
/// <param name="Kills">Killing blows of the player in it.</param>
/// <param name="Assists">Assists of the player in it (the combat log's: fewer than the scoreboard credits).</param>
/// <param name="X">Map coordinate of the middle of the fight (average of the deaths).</param>
/// <param name="Y">Map coordinate of the middle of the fight.</param>
public record FightSummary(
    int StartSeconds,
    int EndSeconds,
    int RadiantDeaths,
    int DireDeaths,
    Team? Winner,
    bool? PlayersTeamWon,
    bool Died,
    int Kills,
    int Assists,
    int X,
    int Y);

/// <summary>The player's item build, read from the replay purchases.</summary>
/// <param name="StartingItems">Items bought before the horn, in purchase order.</param>
/// <param name="Items">Items of the build in the order they were ready: assembled items (also the ones that later became part of another, like Mekansm) and items of 600+ gold bought whole that never became a part; no consumables.</param>
public record PlayerItemBuild(IReadOnlyList<StartingItemCount> StartingItems, IReadOnlyList<TimedItem> Items);

/// <summary>A starting item and how many were bought.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Name">Item name.</param>
/// <param name="Count">How many were bought before the horn.</param>
public record StartingItemCount(int ItemId, string Name, int Count);

/// <summary>An item of the build and when it was ready.</summary>
/// <param name="ItemId">Item.</param>
/// <param name="Name">Item name.</param>
/// <param name="Seconds">Game clock when it got into the inventory (for an assembled item, when its parts came together).</param>
/// <param name="Assembled">Assembled from parts (true) or bought whole (false).</param>
public record TimedItem(int ItemId, string Name, int Seconds, bool Assembled);

/// <summary>A map objective taken: a building that fell, or Roshan/Tormentor killed.</summary>
/// <param name="Kind">Tower, barracks, ancient, roshan or tormentor.</param>
/// <param name="Seconds">Game clock when it fell or died.</param>
/// <param name="TakenBy">The team that took it (destroyed the enemy's building, denied its own, or killed Roshan/Tormentor).</param>
/// <param name="IsPlayersTeam">Taken by the player's team; null when the player is not in the match.</param>
/// <param name="Owner">Buildings: the team that owned it; null otherwise.</param>
/// <param name="TowerTier">Towers: 1 (outer) to 4 (by the Ancient); null otherwise.</param>
/// <param name="Lane">Barracks and towers 1-3: top, middle or bottom; null otherwise.</param>
/// <param name="MeleeBarracks">Barracks: melee (true) or ranged (false); null otherwise.</param>
/// <param name="Denied">A building destroyed by its own team.</param>
/// <param name="KillerHeroName">Hero that dealt the last hit; null when it wasn't a hero (creeps).</param>
/// <param name="AegisHeroName">Roshan: the hero that picked up the Aegis; null otherwise or when nobody did.</param>
public record MapObjectiveSummary(
    ObjectiveKind Kind,
    int Seconds,
    Team TakenBy,
    bool? IsPlayersTeam,
    Team? Owner,
    int? TowerTier,
    Lane? Lane,
    bool? MeleeBarracks,
    bool Denied,
    string? KillerHeroName,
    string? AegisHeroName);

/// <summary>The player's numbers that show where to improve, compared within the match.</summary>
/// <param name="Role">The player's role-queue position, 1 (carry) to 5 (hard support).</param>
/// <param name="CounterpartHeroId">The enemy hero of the same role.</param>
/// <param name="CounterpartHeroName">Its name, or "Unknown hero".</param>
/// <param name="Player">The player's numbers.</param>
/// <param name="Counterpart">The numbers of the enemy of the same role.</param>
/// <param name="MatchAverage">The average of the ten heroes.</param>
/// <param name="Baselines">The player against other players of the same hero and role: pro matches and high-rank public matches of the last 60 days (one entry per source, even when empty); null when the comparison base could not be read.</param>
/// <param name="LaneMatchupHeroId">The opposing lane hero used for the lane matchup baseline, when it could be identified.</param>
/// <param name="LaneMatchupHeroName">Name of the opposing lane hero.</param>
/// <param name="LaneMatchupBaselines">The player against this hero's lane matchup in pro and high-rank matches; each metric needs at least 10 samples, null if the comparison base could not be read.</param>
/// <param name="Insights">What to improve (positioning, then economy, then vision); empty when nothing stands out.</param>
public record SmartStats(
    int Role,
    int CounterpartHeroId,
    string CounterpartHeroName,
    MetricsSummary Player,
    MetricsSummary Counterpart,
    MetricsSummary MatchAverage,
    IReadOnlyList<InsightSummary> Insights,
    IReadOnlyList<BaselineComparisonSummary>? Baselines = null,
    int? LaneMatchupHeroId = null,
    string? LaneMatchupHeroName = null,
    IReadOnlyList<BaselineComparisonSummary>? LaneMatchupBaselines = null);

/// <summary>A hero's numbers (or the match average) for the smart stats.</summary>
/// <param name="Deaths">Deaths (Aegis deaths included).</param>
/// <param name="IsolatedDeaths">Deaths with no ally hero within 1500 map units.</param>
/// <param name="DeathsOnEnemySide">Deaths on the enemy's side of the map (past the river).</param>
/// <param name="EnemySideShare">Share (0..1) of the time alive from 0:00 on the enemy's side; mostly shows which team was winning.</param>
/// <param name="AloneOnEnemySideShare">Share (0..1) of the time alive on the enemy's side with no ally nearby.</param>
/// <param name="NetWorthAt10">Net worth at 10:00; null when the match ended before.</param>
/// <param name="NetWorthAt20">Net worth at 20:00; null when the match ended before.</param>
/// <param name="LastHitsAt10">Last hits at the end of the laning phase.</param>
/// <param name="NetWorthShare">Share (0..1) of the team's net worth at the end.</param>
/// <param name="ObserversPlaced">Observer wards placed.</param>
/// <param name="SentriesPlaced">Sentry wards placed.</param>
/// <param name="ObserverAverageLifetimeSeconds">How long the observers lived on average (360 at most); null without observers.</param>
/// <param name="WardsLostToEnemy">Own wards the enemy destroyed.</param>
/// <param name="EnemyWardsDestroyed">Enemy wards destroyed.</param>
/// <param name="TimeByRegion">Share (0..1) of the time alive from 0:00 in each region of the map; null for metrics computed before the regions.</param>
/// <param name="DeathsByRegion">Deaths in each region of the map; null for metrics computed before the regions.</param>
/// <param name="EstimatedMovementDistance">Estimated straight-line displacement between adjacent position samples, in map units; null when no continuous samples were available.</param>
/// <param name="MovementSampleSegments">Number of adjacent position-sample pairs used for the distance estimate; an average when this summarizes a team.</param>
/// <param name="EstimatedMovementByRegion">Estimated displacement assigned to the region containing each segment midpoint; null when no continuous samples were available.</param>
/// <param name="TimeByRegionByPhase">Share of alive position samples in each map region, split into 0-10, 10-20, and 20+ minute phases; null for older metrics.</param>
/// <param name="DeathsByRegionByPhase">Deaths in each map region, split into 0-10, 10-20, and 20+ minute phases; null for older metrics.</param>
/// <param name="TeamFights">Teamfights of the match (3+ deaths at most 15 s apart); null for older metrics.</param>
/// <param name="FightsNearby">Teamfights the hero was within 2500 map units of (center = average of the fight's deaths); null for older metrics.</param>
/// <param name="KillParticipation">(Kills + combat-log assists) / enemy hero deaths, 0..1; null when no enemy died or for older metrics.</param>
/// <param name="QuickRedeaths">Deaths within 90 s of coming back from the previous death; null for older metrics.</param>
/// <param name="LaneDamageDealt">Damage dealt to enemy heroes in minutes 0-9; null for older metrics.</param>
/// <param name="LaneDamageTaken">Damage taken from enemy heroes in minutes 0-9; null for older metrics.</param>
/// <param name="TeamObserverUptime">Share (0..1) of the match with at least one observer of the hero's team on the map; null for older metrics.</param>
/// <param name="LeadLostPerDeath">Average fall of the team's net worth lead from the minute of each death to the next (positive = lost; correlation, not cause); null without such deaths or for older metrics.</param>
/// <param name="NetWorthGainByRegion">Net worth gained in the minutes spent mostly in each region (divide by MinutesByRegion for a rate per minute); null for older metrics.</param>
/// <param name="MinutesByRegion">Minutes spent mostly in each region (minutes dead all along left out); null for older metrics.</param>
/// <param name="FirstBigItemSeconds">Game-clock time when the first build item worth at least 2000 gold was ready; null when none was recorded.</param>
public record MetricsSummary(
    double Deaths,
    double IsolatedDeaths,
    double DeathsOnEnemySide,
    double EnemySideShare,
    double AloneOnEnemySideShare,
    double? NetWorthAt10,
    double? NetWorthAt20,
    double LastHitsAt10,
    double NetWorthShare,
    double ObserversPlaced,
    double SentriesPlaced,
    double? ObserverAverageLifetimeSeconds,
    double WardsLostToEnemy,
    double EnemyWardsDestroyed,
    RegionValues? TimeByRegion = null,
    RegionValues? DeathsByRegion = null,
    double? EstimatedMovementDistance = null,
    double? MovementSampleSegments = null,
    RegionValues? EstimatedMovementByRegion = null,
    RegionPhaseValues? TimeByRegionByPhase = null,
    RegionPhaseValues? DeathsByRegionByPhase = null,
    double? TeamFights = null,
    double? FightsNearby = null,
    double? KillParticipation = null,
    double? QuickRedeaths = null,
    double? LaneDamageDealt = null,
    double? LaneDamageTaken = null,
    double? TeamObserverUptime = null,
    double? LeadLostPerDeath = null,
    RegionValues? NetWorthGainByRegion = null,
    RegionValues? MinutesByRegion = null,
    double? FirstBigItemSeconds = null);

/// <summary>Values for each region, split into early game (0-10 minutes), mid game (10-20 minutes), and late game (20+ minutes).</summary>
public record RegionPhaseValues(RegionValues EarlyGame, RegionValues MidGame, RegionValues LateGame);

/// <summary>Something to improve, with the numbers behind it.</summary>
/// <param name="Kind">isolatedDeaths, aloneOnEnemySide, behindAtTen, fewObservers, shortObserverLife, deathsInEnemyJungle, lowFightPresence (value = share 0..1 of the team's fights the player was near), quickRedeaths (value = count) lostLaneTrades (value = damage taken from heroes in minutes 0-9; counterpart = the same for the enemy of the same role) or lowObserverUptime (supports; value = share 0..1 of the match the team had an observer up; counterpart = the enemy team's) or gapGrewAfterLane (value = net worth at 20:00; counterpart = the same for the enemy of the same role).</param>
/// <param name="Value">The player's number (deaths, share 0..1, net worth, observers or seconds).</param>
/// <param name="Counterpart">The same number for the enemy of the same role; null when there is none.</param>
public record InsightSummary(InsightKind Kind, double Value, double? Counterpart);

/// <summary>One value per region of the map, seen from the hero's team (bases and jungles are its own or the enemy's; lanes: a band around each lane's path; river: the water of the game's minimap).</summary>
/// <param name="OwnBase">The team's base (behind its tier-3 towers).</param>
/// <param name="EnemyBase">The enemy's base.</param>
/// <param name="TopLane">The top lane.</param>
/// <param name="MiddleLane">The middle lane.</param>
/// <param name="BottomLane">The bottom lane.</param>
/// <param name="River">The river.</param>
/// <param name="OwnJungle">The team's jungle (its side of the river, off the lanes).</param>
/// <param name="EnemyJungle">The enemy's jungle.</param>
public record RegionValues(
    double OwnBase, double EnemyBase, double TopLane, double MiddleLane, double BottomLane, double River, double OwnJungle,
    double EnemyJungle);

/// <summary>The player against one comparison base (same hero and role).</summary>
/// <param name="Source">pro (league matches; roles inferred from the laning) or highRank (public ranked matches of the highest ranks).</param>
/// <param name="Matches">How many matches of the hero in that role the base has.</param>
/// <param name="Metrics">The metrics with enough matches to compare (at least 10); empty while the base is small.</param>
public record BaselineComparisonSummary(BaselineSource Source, int Matches, IReadOnlyList<MetricComparisonSummary> Metrics);

/// <summary>One metric of the player against the base.</summary>
/// <param name="Metric">The metric; counts are per 30 minutes of game (deathsPer30, observersPer30...).</param>
/// <param name="Value">The player's value.</param>
/// <param name="Median">The base's median.</param>
/// <param name="BetterThan">Share (0..1) of the base's matches the player did better than (ties count half; for deaths, fewer is better).</param>
/// <param name="SampleSize">How many of the base's matches have the metric.</param>
public record MetricComparisonSummary(ComparedMetric Metric, double Value, double Median, double BetterThan, int SampleSize);
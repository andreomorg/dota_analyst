using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>Something to improve, with the numbers behind it.</summary>
/// <param name="Kind">isolatedDeaths, aloneOnEnemySide, behindAtTen, fewObservers, shortObserverLife, deathsInEnemyJungle, lowFightPresence (value = share 0..1 of the team's fights the player was near), quickRedeaths (value = count) lostLaneTrades (value = damage taken from heroes in minutes 0-9; counterpart = the same for the enemy of the same role) or lowObserverUptime (supports; value = share 0..1 of the match the team had an observer up; counterpart = the enemy team's) or gapGrewAfterLane (value = net worth at 20:00; counterpart = the same for the enemy of the same role).</param>
/// <param name="Value">The player's number (deaths, share 0..1, net worth, observers or seconds).</param>
/// <param name="Counterpart">The same number for the enemy of the same role; null when there is none.</param>
public record InsightSummary(InsightKind Kind, double Value, double? Counterpart);

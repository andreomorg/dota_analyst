using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

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

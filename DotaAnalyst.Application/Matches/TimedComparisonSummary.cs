using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One statistic of the player at one moment of the match against the base.</summary>
/// <param name="Stat">The statistic (a key of stats.definitions).</param>
/// <param name="Second">The moment, in seconds of the game clock.</param>
/// <param name="Value">The player's value then.</param>
/// <param name="Median">The base's median then.</param>
/// <param name="BetterThan">Share (0..1) of the base's matches the player did better than (ties count half; when less is better, e.g. deaths, less counts as better).</param>
/// <param name="SampleSize">How many of the base's matches lasted until then.</param>
public record TimedComparisonSummary(StatKey Stat, int Second, double Value, double Median, double BetterThan, int SampleSize);

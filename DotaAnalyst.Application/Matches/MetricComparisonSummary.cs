using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>One metric of the player against the base.</summary>
/// <param name="Metric">The metric; counts are per 30 minutes of game (deathsPer30, observersPer30...).</param>
/// <param name="Value">The player's value.</param>
/// <param name="Median">The base's median.</param>
/// <param name="BetterThan">Share (0..1) of the base's matches the player did better than (ties count half; for deaths, fewer is better).</param>
/// <param name="SampleSize">How many of the base's matches have the metric.</param>
public record MetricComparisonSummary(ComparedMetric Metric, double Value, double Median, double BetterThan, int SampleSize);

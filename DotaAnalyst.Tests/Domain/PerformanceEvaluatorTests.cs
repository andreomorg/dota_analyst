using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class PerformanceEvaluatorTests
{
    [Theory]
    [InlineData(1.00, PerformanceRating.Strong)]
    [InlineData(0.70, PerformanceRating.Strong)]   // exactly on the strong threshold
    [InlineData(0.69, PerformanceRating.Average)]
    [InlineData(0.31, PerformanceRating.Average)]
    [InlineData(0.30, PerformanceRating.Weak)]     // exactly on the weak threshold
    [InlineData(0.00, PerformanceRating.Weak)]
    public void Rate_UsesThresholds(double percentile, PerformanceRating expected)
    {
        Assert.Equal(expected, PerformanceEvaluator.Rate(percentile));
    }

    [Fact]
    public void Evaluate_OrdersStrengthsBestFirstAndWeaknessesWorstFirst()
    {
        var player = MatchPlayerBuilder.WithBenchmarks(
            new Benchmark(BenchmarkMetric.GoldPerMinute, 358, 0.51),
            new Benchmark(BenchmarkMetric.DeniesPerMinute, 0.63, 0.97),
            new Benchmark(BenchmarkMetric.TowerDamage, 2200, 0.80),
            new Benchmark(BenchmarkMetric.KillsPerMinute, 0.07, 0.18),
            new Benchmark(BenchmarkMetric.ExperiencePerMinute, 504, 0.25));

        var evaluation = PerformanceEvaluator.Evaluate(player);

        Assert.Equal(5, evaluation.Metrics.Count);
        Assert.Equal(
            [BenchmarkMetric.DeniesPerMinute, BenchmarkMetric.TowerDamage],
            evaluation.Strengths.Select(m => m.Metric));
        Assert.Equal(
            [BenchmarkMetric.KillsPerMinute, BenchmarkMetric.ExperiencePerMinute],
            evaluation.Weaknesses.Select(m => m.Metric));
    }

    [Fact]
    public void Evaluate_IgnoresZeroHealing()
    {
        var player = MatchPlayerBuilder.WithBenchmarks(
            new Benchmark(BenchmarkMetric.HeroHealingPerMinute, 0, 0.70));

        Assert.Empty(PerformanceEvaluator.Evaluate(player).Metrics);
    }

    [Fact]
    public void Evaluate_KeepsHealingWhenHeroHealed()
    {
        var player = MatchPlayerBuilder.WithBenchmarks(
            new Benchmark(BenchmarkMetric.HeroHealingPerMinute, 150, 0.10));

        Assert.Equal(BenchmarkMetric.HeroHealingPerMinute, Assert.Single(PerformanceEvaluator.Evaluate(player).Weaknesses).Metric);
    }

    [Fact]
    public void Evaluate_IsEmptyWithoutBenchmarks()
    {
        var evaluation = PerformanceEvaluator.Evaluate(MatchPlayerBuilder.WithBenchmarks());

        Assert.Empty(evaluation.Metrics);
        Assert.Empty(evaluation.Strengths);
        Assert.Empty(evaluation.Weaknesses);
    }
}

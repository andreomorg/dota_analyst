using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Tests.Domain;

public class InsightGeneratorTests
{
    // A quiet game: nothing to point out.
    private static readonly HeroMetrics Quiet = new(
        HeroId: 1, Team.Radiant, Role: 5, Deaths: 4, IsolatedDeaths: 1, DeathsOnEnemySide: 0, EnemySideShare: 0.2,
        AloneOnEnemySideShare: 0.02, NetWorthAt10: 2000, NetWorthAt20: 5000, LastHitsAt10: 10, NetWorthShare: 0.12,
        ObserversPlaced: 8, SentriesPlaced: 6, ObserverAverageLifetimeSeconds: 300, WardsLostToEnemy: 2, EnemyWardsDestroyed: 3);

    private static readonly HeroMetrics Counterpart = Quiet with { HeroId = 6, Team = Team.Dire };

    [Fact]
    public void AQuietGame_HasNothingToImprove()
    {
        Assert.Empty(InsightGenerator.Generate(Quiet, Counterpart));
    }

    [Theory]
    [InlineData(2, 5, true)]   // 40%
    [InlineData(2, 6, false)]  // 33%
    [InlineData(1, 1, false)]  // one death is not a pattern
    public void IsolatedDeaths_AtLeastTwoAndFortyPercent(int isolated, int deaths, bool expected)
    {
        var insights = InsightGenerator.Generate(Quiet with { IsolatedDeaths = isolated, Deaths = deaths }, Counterpart);

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.IsolatedDeaths));
    }

    [Theory]
    [InlineData(0.15, 0.05, 2, true)]
    [InlineData(0.15, 0.12, 2, false)] // the counterpart did it too
    [InlineData(0.08, 0.0, 2, false)]  // too little
    [InlineData(0.23, 0.03, 0, false)] // a carry farming the enemy's jungle without dying (Lifestealer, 9042408185)
    public void AloneOnEnemySide_ComparedToTheCounterpart_WhenItCostDeaths(double mine, double theirs, int deathsThere, bool expected)
    {
        var insights = InsightGenerator.Generate(
            Quiet with { AloneOnEnemySideShare = mine, DeathsOnEnemySide = deathsThere },
            Counterpart with { AloneOnEnemySideShare = theirs });

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.AloneOnEnemySide));
    }

    [Fact]
    public void BehindAtTen_WithTheNumbers()
    {
        var insights = InsightGenerator.Generate(Quiet with { NetWorthAt10 = 1600 }, Counterpart with { NetWorthAt10 = 2000 });

        Assert.Equal([new Insight(InsightKind.BehindAtTen, 1600, 2000)], insights);
    }

    [Fact]
    public void FewObservers_OnlyForSupports()
    {
        var support = InsightGenerator.Generate(Quiet with { ObserversPlaced = 5 }, Counterpart with { ObserversPlaced = 8 });
        var carry = InsightGenerator.Generate(Quiet with { Role = 1, ObserversPlaced = 0 }, Counterpart with { Role = 1, ObserversPlaced = 3 });

        Assert.Contains(support, insight => insight.Kind == InsightKind.FewObservers);
        Assert.DoesNotContain(carry, insight => insight.Kind == InsightKind.FewObservers);
    }

    [Theory]
    [InlineData(2, 170, true)]
    [InlineData(2, 180, false)]
    [InlineData(1, 60, false)] // a single observer says little
    public void ShortObserverLife_WithAtLeastTwoObservers(int placed, int lifetime, bool expected)
    {
        var insights = InsightGenerator.Generate(
            Quiet with { ObserversPlaced = placed, ObserverAverageLifetimeSeconds = lifetime }, Counterpart with { ObserversPlaced = placed });

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.ShortObserverLife));
    }
}

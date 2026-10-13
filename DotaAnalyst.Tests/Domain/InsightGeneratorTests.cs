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

    [Theory]
    [InlineData(2, 1, true)]
    [InlineData(2, 2, false)] // the counterpart died as much in yours
    [InlineData(1, 0, false)] // one death is not a pattern
    public void DeathsInEnemyJungle_MoreThanTheCounterpart(int mine, int theirs, bool expected)
    {
        var insights = InsightGenerator.Generate(
            Quiet with { DeathsByRegion = PerRegion<int>.Of(region => region == MapRegion.EnemyJungle ? mine : 0) },
            Counterpart with { DeathsByRegion = PerRegion<int>.Of(region => region == MapRegion.EnemyJungle ? theirs : 0) });

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.DeathsInEnemyJungle));
    }

    [Theory]
    [InlineData(4, 1, 4, 3, true)]    // 25% against 75%
    [InlineData(4, 1, 4, 1, false)]   // the counterpart was away too
    [InlineData(2, 0, 2, 2, false)]   // two fights say little
    [InlineData(4, 3, 4, 4, false)]   // 75%: present enough
    public void LowFightPresence_AgainstTheCounterpart(int fights, int near, int theirFights, int theirNear, bool expected)
    {
        var insights = InsightGenerator.Generate(
            Quiet with { TeamFights = fights, FightsNearby = near },
            Counterpart with { TeamFights = theirFights, FightsNearby = theirNear });

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.LowFightPresence));
    }

    [Theory]
    [InlineData(2, 0, true)]
    [InlineData(2, 2, false)]
    [InlineData(1, 0, false)]
    public void QuickRedeaths_MoreThanTheCounterpart(int mine, int theirs, bool expected)
    {
        var insights = InsightGenerator.Generate(Quiet with { QuickRedeaths = mine }, Counterpart with { QuickRedeaths = theirs });

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.QuickRedeaths));
    }

    [Theory]
    [InlineData(2000, 600, 1200, true)]
    [InlineData(2000, 2100, 1200, false)] // dealt more than taken: a fair trade
    [InlineData(2000, 600, 1500, false)]  // the counterpart took about as much
    [InlineData(1400, 100, 200, false)]   // too little to matter
    public void LostLaneTrades_AgainstTheCounterpart(int taken, int dealt, int theirTaken, bool expected)
    {
        var insights = InsightGenerator.Generate(
            Quiet with { LaneDamageTaken = taken, LaneDamageDealt = dealt },
            Counterpart with { LaneDamageTaken = theirTaken, LaneDamageDealt = 500 });

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.LostLaneTrades));
    }

    [Theory]
    [InlineData(1800, 4000, 2000, 6000, true)]   // 90% at 10:00, 67% at 20:00: the gap grew
    [InlineData(1500, 4500, 2000, 6000, false)]  // 75% then 75%: behind, but it didn't grow
    [InlineData(2000, 5000, 2000, 5500, false)]  // 91% at 20:00: close enough
    public void GapGrewAfterLane_AgainstTheCounterpart(int mine10, int mine20, int theirs10, int theirs20, bool expected)
    {
        var insights = InsightGenerator.Generate(
            Quiet with { NetWorthAt10 = mine10, NetWorthAt20 = mine20 },
            Counterpart with { NetWorthAt10 = theirs10, NetWorthAt20 = theirs20 });

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.GapGrewAfterLane));
    }

    [Fact]
    public void FewObservers_NotWhenTheTeamStillHadMoreVision()
    {
        // The Dark Willow of 9042303984: 9 observers against 16, but her team had vision 97% of the match against 65%.
        var insights = InsightGenerator.Generate(
            Quiet with { ObserversPlaced = 9, TeamObserverUptime = 0.97 },
            Counterpart with { ObserversPlaced = 16, TeamObserverUptime = 0.65 });

        Assert.DoesNotContain(insights, insight => insight.Kind == InsightKind.FewObservers);
    }

    [Theory]
    [InlineData(5, 0.66, 0.83, true)]  // the Lich of 9034320024
    [InlineData(5, 0.60, 0.70, false)] // the enemy team had little vision too
    [InlineData(5, 0.80, 0.98, false)] // 80% is enough
    [InlineData(1, 0.10, 0.80, false)] // a carry isn't judged on the team's wards
    public void LowObserverUptime_ForSupports(int role, double uptime, double theirUptime, bool expected)
    {
        var insights = InsightGenerator.Generate(
            Quiet with { Role = role, TeamObserverUptime = uptime }, Counterpart with { Role = role, TeamObserverUptime = theirUptime });

        Assert.Equal(expected, insights.Any(insight => insight.Kind == InsightKind.LowObserverUptime));
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

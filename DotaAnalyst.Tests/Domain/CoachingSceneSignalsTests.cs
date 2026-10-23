using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Tests.Domain;

public class CoachingSceneSignalsTests
{
    public static TheoryData<string, CoachingSignalFacts, string[]> Cases => new()
    {
        { "R1", new(VisibleEnemies: 2, Allies: 0), ["alliesWithinFightRange", "visibleEnemiesWithinFightRange"] },
        { "R2", new(HealthShare: .3, VisibleEnemies: 1), ["healthShare", "visibleEnemiesWithinFightRange", "nearestVisibleEnemyDistance"] },
        { "R3", new(EscapeReady: true, Damage: 1), ["damageFromHeroesLast6Seconds"] },
        { "R4", new(OnEnemySide: true, AllyDistance: 1501, MissingEnemies: 3), ["nearestAllyDistance", "missingEnemies", "missingEnemyLastSeenSecondsAgo"] },
        { "R5", new(AheadOfWave: 1, ObserverVision: false, MissingEnemies: 2), ["missingEnemies"] },
        { "R6", new(BuildingsUnderAttack: 1, TeleportReady: true), ["ownBuildingsUnderAttack"] },
        { "R7", new(UltimateReady: true, VisibleEnemies: 2), ["visibleEnemiesWithinFightRange"] },
        { "R8", new(RoshanAlive: true, NetWorthLead: 3000, HasAegis: false), ["teamNetWorthLead"] },
        { "R9", new(DestinationOnEnemySide: true, MissingEnemies: 3, TowerSeenShare: .499), ["intentDestinationAdvance", "missingEnemies", "towerSeenShare"] },
        { "R10", new(TowerNearestEnemy: 1200, HealthShare: .5), ["healthShare", "towerPathLength", "towerNearestSeenEnemy", "allyPathLength"] },
        { "R11", new(AllyPings: 2, AllyDistance: 1501), ["allyPingsNearLast10", "nearestAllyDistance"] },
    };

    [Theory, MemberData(nameof(Cases))]
    public void EachRuleUsesExactlyItsPolicyNumbers(string id, CoachingSignalFacts facts, string[] keys)
    {
        var signal = Assert.Single(CoachingSceneSignals.Of(facts));
        Assert.Equal(id, signal.Id);
        Assert.Equal(keys, signal.Numbers.Keys);
        // Removing each required input must prevent the rule (unknown is never a positive observation).
        foreach (var property in typeof(CoachingSignalFacts).GetProperties().Where(p => p.GetValue(facts) is not null))
        {
            var missing = facts with { };
            property.SetValue(missing, null);
            Assert.DoesNotContain(CoachingSceneSignals.Of(missing), s => s.Id == id);
        }
    }

    [Fact]
    public void EmptyAndBoundaryCasesDoNotFire()
    {
        Assert.Empty(CoachingSceneSignals.Of(new()));
        Assert.Empty(CoachingSceneSignals.Of(new(VisibleEnemies: 2, Allies: 1, HealthShare: .301,
            AllyDistance: 1500, AllyPings: 2, MissingEnemies: 3, DestinationOnEnemySide: true, TowerSeenShare: .5)));
    }

    [Fact]
    public void SignalsAreInPolicyPriorityAndKeepRawNumbers()
    {
        var signals = CoachingSceneSignals.Of(new(VisibleEnemies: 3, Allies: 0, HealthShare: .25,
            EscapeReady: true, Damage: 123, UltimateReady: true, TowerNearestEnemy: 900));
        Assert.Equal(["R1", "R2", "R3", "R7", "R10"], signals.Select(s => s.Id));
        Assert.Equal(.25, signals[1].Numbers["healthShare"]);
        Assert.Null(signals[1].Numbers["nearestVisibleEnemyDistance"]);
        Assert.Equal(123, signals[2].Numbers["damageFromHeroesLast6Seconds"]);
    }
}

using DotaAnalyst.Domain.Coaching;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotaAnalyst.Tests.Domain;

public class CoachingSceneSignalsTests
{
    [Fact]
    public void SharedPythonFixturesMatchActualDomainSignals()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Fixtures", "coaching-signals.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var fixture in document.RootElement.EnumerateArray())
        {
            var facts = fixture.GetProperty("facts").Deserialize<CoachingSignalFacts>(options)!;
            var actual = JsonSerializer.SerializeToNode(CoachingSceneSignals.Of(facts), options);
            var expected = JsonNode.Parse(fixture.GetProperty("signals").GetRawText());
            Assert.True(JsonNode.DeepEquals(expected, actual), fixture.GetProperty("case").GetString());
        }
    }

    public static TheoryData<string, CoachingSignalFacts, string, double, bool, bool, bool> Boundaries => new()
    {
        { "R1", new(Allies: 1), "VisibleEnemies", 2, false, false, true },
        { "R1", new(VisibleEnemies: 3), "Allies", 2, true, false, false },
        { "R2", new(VisibleEnemies: 1), "HealthShare", .30, true, true, false },
        { "R2", new(HealthShare: .2), "VisibleEnemies", 1, false, true, true },
        { "R3", new(EscapeReady: true), "Damage", 0, false, false, true },
        { "R4", new(OnEnemySide: true, MissingEnemies: 3), "AllyDistance", 1500, false, false, true },
        { "R4", new(OnEnemySide: true, AllyDistance: 1600), "MissingEnemies", 3, false, true, true },
        { "R5", new(ObserverVision: false, MissingEnemies: 2), "AheadOfWave", 0, false, false, true },
        { "R5", new(ObserverVision: false, AheadOfWave: 1), "MissingEnemies", 2, false, true, true },
        { "R6", new(TeleportReady: true), "BuildingsUnderAttack", 1, false, true, true },
        { "R7", new(UltimateReady: true), "VisibleEnemies", 2, false, true, true },
        { "R8", new(RoshanAlive: true, HasAegis: false), "NetWorthLead", 3000, false, true, true },
        { "R9", new(DestinationOnEnemySide: true, TowerSeenShare: .2), "MissingEnemies", 3, false, true, true },
        { "R9", new(DestinationOnEnemySide: true, MissingEnemies: 3), "TowerSeenShare", .5, true, false, false },
        { "R10", new(HealthShare: .5), "TowerNearestEnemy", 1200, true, true, false },
        { "R10", new(TowerNearestEnemy: 1000), "HealthShare", .5, true, true, false },
        { "R11", new(AllyDistance: 1600), "AllyPings", 2, false, true, true },
        { "R11", new(AllyPings: 2), "AllyDistance", 1500, false, false, true },
    };

    [Theory, MemberData(nameof(Boundaries))]
    public void OneChangedNumericFactCrossesOnlyTheDocumentedBoundary(string id, CoachingSignalFacts facts,
        string field, double boundary, bool below, bool equal, bool above)
    {
        var property = typeof(CoachingSignalFacts).GetProperty(field)!;
        var type = Nullable.GetUnderlyingType(property.PropertyType)!;
        var step = type == typeof(int) ? 1 : .001;
        foreach (var (value, expected) in new[] { (boundary - step, below), (boundary, equal), (boundary + step, above) })
        {
            var changed = facts with { };
            property.SetValue(changed, Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(expected, CoachingSceneSignals.Of(changed).Any(signal => signal.Id == id));
        }
        Assert.DoesNotContain(CoachingSceneSignals.Of(facts), signal => signal.Id == id);
    }

    public static TheoryData<string, CoachingSignalFacts, string, bool> BooleanGates => new()
    {
        { "R3", new(Damage: 1), "EscapeItemReady", true },
        { "R3", new(Damage: 1), "EscapeReady", true },
        { "R4", new(AllyDistance: 1600, MissingEnemies: 3), "OnEnemySide", true },
        { "R5", new(AheadOfWave: 1, MissingEnemies: 2), "ObserverVision", false },
        { "R6", new(BuildingsUnderAttack: 1), "TeleportReady", true },
        { "R7", new(VisibleEnemies: 2), "UltimateReady", true },
        { "R8", new(NetWorthLead: 3000, HasAegis: false), "RoshanAlive", true },
        { "R8", new(NetWorthLead: 3000, RoshanAlive: true), "HasAegis", false },
        { "R9", new(MissingEnemies: 3, TowerSeenShare: .2), "DestinationOnEnemySide", true },
    };

    [Theory, MemberData(nameof(BooleanGates))]
    public void UnknownAndOppositeBooleanNeverCountAsTheRequiredObservation(string id, CoachingSignalFacts facts,
        string field, bool required)
    {
        var property = typeof(CoachingSignalFacts).GetProperty(field)!;
        foreach (var value in new bool?[] { null, !required, required })
        {
            var changed = facts with { };
            property.SetValue(changed, value);
            Assert.Equal(value == required, CoachingSceneSignals.Of(changed).Any(signal => signal.Id == id));
        }
    }

    [Fact]
    public void EitherEscapeSourceSufficesAndIrrelevantFactsDoNotChangeSignals()
    {
        var both = CoachingSceneSignals.Of(new(EscapeItemReady: true, EscapeReady: true, Damage: 1));
        Assert.Single(both);
        var facts = new CoachingSignalFacts(RoshanAlive: true, HasAegis: false, NetWorthLead: 3000);
        var original = Assert.Single(CoachingSceneSignals.Of(facts));
        var changed = Assert.Single(CoachingSceneSignals.Of(facts with { NearestEnemyDistance = 1, LastSeenSeconds = 99 }));
        Assert.Equal(original.Id, changed.Id);
        Assert.Equal(original.Numbers, changed.Numbers);
        Assert.DoesNotContain(typeof(CoachingSignalFacts).GetProperties(), p => p.Name.StartsWith("Hidden", StringComparison.Ordinal));
    }

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

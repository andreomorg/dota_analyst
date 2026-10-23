using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public class BuildCoachingContextTests
{
    [Fact]
    public void ShortMatchDoesNotInventTenOrTwentyMinuteEvidence()
    {
        MatchId.TryCreate(1, out var matchId);
        var metrics = new HeroMetrics(1, Team.Radiant, 5, 0, 0, 0, 0, 0, null, null, 0, 0, 0, 0, null, 0, 0, RoleInferred: true);
        var replay = new ReplayAnalysis(matchId, 300, [],
            new MatchTimeline([0, 300], [new HeroTimeline(1, Team.Radiant, [0, 100], [0, 100])]), metrics: [metrics]);
        var context = BuildCoachingContext.Build(replay, 1, new CoachingPatch(null, "unknown", null, false));
        Assert.DoesNotContain(context.Evidence, evidence => evidence.Metric is "NetWorthAt10" or "NetWorthAt20" or "LastHitsAt10");
        Assert.True(context.Role.IsInferred);
        Assert.Equal(5, context.Role.Position);
        Assert.Null(context.Patch.MatchPatch);
        Assert.False(context.Patch.MechanicsVerified);
        Assert.All(context.Evidence, evidence => Assert.InRange(evidence.ToSeconds, 0, 300));
    }
    private static ReplayAnalysis Replay(HeroMetrics metrics)
    {
        MatchId.TryCreate(1, out var id);
        var players = Enumerable.Range(1, 10).Select(hero => new LaningStats(hero,
            hero <= 5 ? Team.Radiant : Team.Dire, Lane.Middle, 0, 0, 0, 0, 0)).ToArray();
        return new(id, 300, players,
            new MatchTimeline([0, 300], [new HeroTimeline(1, Team.Radiant, [0, 100], [0, 100])]),
            deaths: [new(1, 30, 0, 0, KillerKind.Other, null, false),
                new(1, 90, 0, 0, KillerKind.Other, null, false)], metrics: [metrics]);
    }

    private static HeroMetrics Metrics() => new(1, Team.Radiant, 5, 2, 2, 0, 0, 0,
        null, null, 0, 0, 0, 0, null, 0, 0);

    [Fact]
    public void V5IncludesEveryNumericFieldOfAllBlocksWithoutChangingV4()
    {
        var metrics = Metrics() with
        {
            Terrain = new(.1, .2, .3, .4, .5, 1, 2, 3, 4),
            Vision = new(.2, 1, 2, 3, 4, .5),
            Objectives = new(new(1, 2, 3, 4, 5, 6, 7, 8, 9), 1, 2, 3, 4, 5, 6, 7, 8),
            Teleports = new(1, 2, 3), Plays = new(1, 2, 3, 4, 5),
            DeathContext = new(2, 1, 1, 1, 1, 3.5), ItemUsage = new(1, 2, 3, 4, 5),
            FightInitiation = new(1, 2, 3), LanePressure = new(10, .2, -250),
            DayNight = new(.5, 1, 2, 3, 4), Smokes = new(1, 2), Farm = new(1, 2, 3, 4, 5)
        };
        var replay = Replay(metrics);
        var patch = new CoachingPatch(null, "unknown", null, false);
        var v4 = BuildCoachingContext.Build(replay, 1, patch);
        var v5 = BuildCoachingContext.BuildV5(replay, 1, patch);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(BuildCoachingContext.Build(Replay(Metrics()), 1, patch)),
            System.Text.Json.JsonSerializer.Serialize(v4));
        Assert.Equal(4, v4.SchemaVersion);
        Assert.Equal(5, v5.SchemaVersion);
        Assert.DoesNotContain(v4.Evidence, e => e.Metric.Contains('.'));
        Assert.Contains("No player camera, attention or reconstructed visibility.", v4.Limitations);
        Assert.Equal(v4.Evidence.Select(e => e with { Limitation = "" }),
            v5.Evidence.Take(v4.Evidence.Count).Select(e => e with { Limitation = "" }));
        var blocks = new[] { "Terrain", "Vision", "Objectives", "Teleports", "Plays", "DeathContext",
            "ItemUsage", "FightInitiation", "LanePressure", "DayNight", "Smokes", "Farm" };
        void CheckFields(object block, string prefix)
        {
            foreach (var property in block.GetType().GetProperties())
            {
                var value = property.GetValue(block);
                if (value is int or double)
                {
                    var fact = Assert.Single(v5.Evidence, e => e.Metric == $"{prefix}.{property.Name}");
                    Assert.Equal(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), fact.Value);
                    Assert.Equal(0, fact.FromSeconds);
                    Assert.Equal(300, fact.ToSeconds);
                    Assert.False(string.IsNullOrWhiteSpace(fact.Unit));
                    Assert.False(string.IsNullOrWhiteSpace(fact.Limitation));
                }
                else if (value is MapObjectiveCounts) CheckFields(value, prefix + ".Counts");
            }
        }
        foreach (var block in blocks) CheckFields(typeof(HeroMetrics).GetProperty(block)!.GetValue(metrics)!, block);
        Assert.Equal("map-units", Assert.Single(v5.Evidence, e => e.Metric == "LanePressure.AverageMeetingPoint").Unit);
        Assert.Equal(v5.Evidence.Count, v5.Evidence.Select(e => e.Id).Distinct().Count());
        Assert.DoesNotContain(v5.Limitations.Concat(v5.Evidence.Select(e => e.Limitation)),
            text => text.Contains("no cooldown state", StringComparison.OrdinalIgnoreCase)
                || text.Contains("No patch-compatible population baseline", StringComparison.OrdinalIgnoreCase)
                || text.Contains("No player camera, attention or reconstructed visibility", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void V5OmitsNullBlocksAndNullNumbersButKeepsMeasuredZero()
    {
        var metrics = Metrics() with { Teleports = new(0, null, null), Vision = new(0, null, null, null, null) };
        var context = BuildCoachingContext.BuildV5(Replay(metrics), 1, new(null, "unknown", null, false));
        var added = context.Evidence.Where(e => e.Id.StartsWith("v5-", StringComparison.Ordinal)).ToArray();
        Assert.Collection(added, e => Assert.Equal("Vision.SeenByEnemiesShare", e.Metric),
            e => Assert.Equal("Teleports.Teleports", e.Metric));
        Assert.All(added, e => Assert.Equal(0, e.Value));
    }

    [Fact]
    public void V5UsesTheSameNonemptyFindingsAndChoiceValidationAsV4()
    {
        var replay = Replay(Metrics());
        HeroPositions[] positions = [.. Enumerable.Range(2, 4).Select(hero =>
            new HeroPositions(hero, [new(30, 5000, 5000), new(90, 5000, 5000)]))];
        var patch = new CoachingPatch(null, "unknown", null, false);
        var v4 = BuildCoachingContext.Build(replay, 1, patch, positions);
        var v5 = BuildCoachingContext.BuildV5(replay, 1, patch, positions);
        Assert.NotEmpty(v4.Findings!);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(v4.Findings),
            System.Text.Json.JsonSerializer.Serialize(v5.Findings));
        var finding = v5.Findings![0];
        Assert.Empty(CoachingFindingSelector.ValidateChoices(v5, [new(finding.Id, finding.ExerciseIds[0])]));
        var forged = v5 with { Evidence = v5.Evidence.Where(e => e.Metric != "ReviewedDeaths").ToArray() };
        Assert.Empty(CoachingFindingSelector.Select(forged));
        Assert.NotEmpty(CoachingFindingSelector.ValidateChoices(forged, []));
    }
}
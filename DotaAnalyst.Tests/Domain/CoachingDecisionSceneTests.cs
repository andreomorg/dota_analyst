using System.Text.Json;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Tests.Infrastructure;

namespace DotaAnalyst.Tests.Domain;

public class CoachingDecisionSceneTests
{
    private static ReplayAnalysis Replay(bool withEvents = false)
    {
        MatchId.TryCreate(1, out var id);
        var players = new[] { new LaningStats(1, Team.Radiant, Lane.Middle, 0, 0, 0, 0, 0),
            new LaningStats(2, Team.Dire, Lane.Middle, 0, 0, 0, 0, 0) };
        return new(id, 60, players, new MatchTimeline([0, 60],
            [new HeroTimeline(1, Team.Radiant, [100, 500], [0, 0])]),
            purchases: withEvents ? [new(1, 29, "boots"), new(1, 30, "blink"), new(1, 31, "tpscroll")] : null,
            abilityUpgrades: withEvents ? [new(1, 29, "test_a", 1), new(1, 30, "test_b", 1), new(1, 31, "test_c", 1)] : null,
            objectives: withEvents ? [new(ObjectiveKind.Roshan, 29, Team.Radiant, 1),
                new(ObjectiveKind.Roshan, 30, Team.Radiant, 1), new(ObjectiveKind.Roshan, 31, Team.Radiant, 1)] : null);
    }

    private static CoachingEpisode Episode(int seconds = 40) => new("death", CoachingEpisodeKind.Death,
        1, seconds, Math.Max(0, seconds - 15), 55, new(4, 0), null, null, false, new(0, 60, 400, null));

    [Fact]
    public void FuturePositionAndEconomicSnapshotCannotFillEarlierAnchor()
    {
        var scene = Assert.Single(CoachingDecisionSceneBuilder.Build(Replay(), 1, [Episode()],
            [new(1, [new(29, 10, 20), new(31, 900, 900)]), new(2, [new(31, 100, 100)])]));
        Assert.Equal(30, scene.AtOrBeforeAnchor.AnchorSeconds);
        var position = Assert.Single(scene.AtOrBeforeAnchor.Positions);
        Assert.Equal(29, position.SampleSeconds);
        Assert.Equal(1, scene.AtOrBeforeAnchor.MissingHeroPositions);
        Assert.Equal(new CoachingEconomySnapshot(0, 100, null), scene.AtOrBeforeAnchor.Economy);
        Assert.Equal(60, scene.RetrospectiveEpisode.Economy!.ToSeconds);
    }

    [Fact]
    public void PurchasesUpgradesAndObjectivesAfterAnchorAreExcluded()
    {
        var scene = Assert.Single(CoachingDecisionSceneBuilder.Build(Replay(true), 1, [Episode()], null));
        Assert.Equal(2, scene.AtOrBeforeAnchor.RecentPurchases!.Count);
        Assert.Equal(2, scene.AtOrBeforeAnchor.RecentAbilityUpgrades!.Count);
        Assert.Equal(2, scene.AtOrBeforeAnchor.RecentObjectives!.Count);
        Assert.All(scene.AtOrBeforeAnchor.RecentObjectives, o => Assert.True(o.Seconds <= 30));
    }

    [Fact]
    public void MissingCollectionsStayUnknownAndOldPositionsAreNotReused()
    {
        var scene = Assert.Single(CoachingDecisionSceneBuilder.Build(Replay(), 1, [Episode()],
            [new(1, [new(27, 10, 20)])]));
        Assert.Empty(scene.AtOrBeforeAnchor.Positions);
        Assert.Null(scene.AtOrBeforeAnchor.RecentPurchases);
        Assert.Null(scene.AtOrBeforeAnchor.RecentAbilityUpgrades);
        Assert.Null(scene.AtOrBeforeAnchor.RecentObjectives);
    }

    [Fact]
    public void EarlyEventHasZeroAnchorWithoutNegativeOrFuturePosition()
    {
        var scene = Assert.Single(CoachingDecisionSceneBuilder.Build(Replay(), 1, [Episode(5)],
            [new(1, [new(-1, 10, 20), new(1, 20, 30)])]));
        Assert.Equal(0, scene.AtOrBeforeAnchor.AnchorSeconds);
        Assert.Empty(scene.AtOrBeforeAnchor.Positions);
    }

    [Fact]
    public void FactoryKeepsMatchIdentityAndPersonalFieldsOutOfDecisionContext()
    {
        MatchId.TryCreate(9034320024, out var id);
        var document = ReplayParserClientTests.MatchJson().Replace("\"kills\": []", """
            "kills": [{"t":330,"victim":0,"x":0,"y":0,"assists":[]}]
            """);
        var context = ReplayCoachingContextFactory.CreateDecision(document, id, 31, new(null, "test", null, false));
        Assert.Equal(id.ToString(), context.MatchId);
        Assert.Equal(320, Assert.Single(context.Scenes).AtOrBeforeAnchor.AnchorSeconds);
        var json = JsonSerializer.Serialize(context);
        Assert.DoesNotContain("steamId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"name\"", json, StringComparison.OrdinalIgnoreCase);
        MatchId.TryCreate(2, out var wrong);
        Assert.Throws<InvalidDataException>(() => ReplayCoachingContextFactory.CreateDecision(document, wrong, 31, context.Patch));
    }
}

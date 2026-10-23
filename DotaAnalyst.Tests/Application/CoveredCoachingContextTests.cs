using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Tests.Infrastructure;

namespace DotaAnalyst.Tests.Application;

public class CoveredCoachingContextTests
{
    private static CoachingEpisode Death(string id, bool? isolated) => new(id, CoachingEpisodeKind.Death,
        1, 330, 315, 345, new(4, isolated is null ? 0 : 4), isolated, null, false, null);

    private static CoachingEpisode Fight(string id, bool? near) => new(id, CoachingEpisodeKind.DetectedFight,
        1, 330, 325, 340, new(16, near is null ? 0 : 16), null, near, false, null);

    private static CoachingContext Context(params CoachingEpisode[] episodes)
    {
        var context = new CoachingContext(4, "1", 1, new(1, "test", false), new(null, "unknown", null, false),
            600, "omniscient-retrospective", CoachingEpisodeFacts.Build(episodes, 600), [], Episodes: episodes);
        return context with { Findings = CoachingFindingSelector.Select(context) };
    }

    [Fact]
    public void ConfirmedAndUnknownDeathsArePresentedSeparately()
    {
        var context = Context(Death("d1", true), Death("d2", true), Death("d3", null));
        var finding = Assert.Single(context.Findings!);
        Assert.Equal("3", finding.RuleVersion);
        Assert.Equal(2, finding.EpisodeIds!.Count);
        var suggestion = Assert.Single(RenderCoachingAdvice.Render(context,
            [new(finding.Id, finding.ExerciseIds.Single())]).Suggestions);
        Assert.Contains("Entre 3 mortes", suggestion.Interpretation);
        Assert.Contains("2 tiveram quatro aliados", suggestion.Interpretation);
        Assert.Contains("em 1, o isolamento permaneceu desconhecido", suggestion.Interpretation);
    }

    [Fact]
    public void LegacyCountsCannotTurnUnknownDeathsOrReturnEstimatesIntoAdvice()
    {
        var context = Context(Death("d1", null), Death("d2", null), Death("d3", null));
        context = context with
        {
            Evidence = [.. context.Evidence,
            new("legacy-deaths", "Deaths", 10, "count", 0, 600, "legacy", ""),
            new("legacy-isolated", "IsolatedDeaths", 10, "count", 0, 600, "legacy", ""),
            new("legacy-return", "QuickRedeaths", 10, "count", 0, 600, "legacy", "")]
        };
        Assert.Empty(CoachingFindingSelector.Select(context));
        Assert.Empty(RenderCoachingAdvice.Render(context, []).Suggestions);
    }

    [Fact]
    public void MissingFightCoverageCannotBeConfusedWithConfirmedDistance()
    {
        Assert.Empty(Context(Fight("f1", null), Fight("f2", null), Fight("f3", null)).Findings!);
        var context = Context(Fight("f1", false), Fight("f2", true), Fight("f3", null));
        var finding = Assert.Single(context.Findings!);
        Assert.Equal(new[] { "f1" }, finding.EpisodeIds);
        var suggestion = Assert.Single(RenderCoachingAdvice.Render(context,
            [new(finding.Id, finding.ExerciseIds.Single())]).Suggestions);
        Assert.Contains("em 1, a proximidade permaneceu desconhecida", suggestion.Interpretation);
    }

    [Fact]
    public void ForgedEpisodeCountsAreRejectedEvenWithNoChoices()
    {
        var context = Context(Death("d1", true), Death("d2", true));
        context = context with
        {
            Evidence = context.Evidence.Select(e =>
            e.Metric == "ConfirmedIsolatedDeaths" ? e with { Value = 100 } : e).ToArray()
        };
        Assert.NotEmpty(CoachingFindingSelector.ValidateChoices(context, []));
        Assert.Throws<InvalidDataException>(() => RenderCoachingAdvice.Render(context, []));
    }

    [Fact]
    public void NewFactoryContextExcludesPreHornDeathsWithoutChangingReplayMetrics()
    {
        MatchId.TryCreate(9034320024, out var matchId);
        var document = ReplayParserClientTests.MatchJson().Replace("\"kills\": []", """
            "kills": [{"t":-10,"victim":0,"x":0,"y":0,"reincarnated":false,"assists":[]},
                      {"t":330,"victim":0,"x":0,"y":0,"reincarnated":false,"assists":[]}]
            """);
        var context = ReplayCoachingContextFactory.Create(document, matchId, 31, new(null, "test", null, false));
        Assert.Equal(4, context.SchemaVersion);
        Assert.Single(context.Episodes!);
        Assert.Equal(1, context.Evidence.Single(e => e.Metric == "ReviewedDeaths").Value);
        Assert.Equal(1, context.Evidence.Single(e => e.Metric == "UnknownIsolationDeaths").Value);
        Assert.DoesNotContain(context.Evidence, e => e.Metric is "Deaths" or "IsolatedDeaths" or "QuickRedeaths");
        Assert.Empty(context.Findings!);
    }
}

using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Tests.Application;

public class CoachingEpisodeIntegrationTests
{
    private static CoachingContext Context(bool? isolated, int observed = 4)
    {
        var context = new CoachingContext(3, "1", 1, new(1, "replay", false), new(null, "unknown", null, false),
            600, "omniscient-retrospective",
            [new("d", "Deaths", 3, "count", 0, 600, "replay", ""),
             new("i", "IsolatedDeaths", 3, "count", 0, 600, "replay", "")], [],
            Episodes: [new("death-1-330-0", CoachingEpisodeKind.Death, 1, 330, 315, 345,
                new(4, observed), isolated, null, false, new(300, 360, -50, 2))]);
        return context with { Findings = CoachingFindingSelector.Select(context) };
    }

    [Fact]
    public void ConfirmedEpisodeHasRealClockAndSeparateEconomicWindow()
    {
        var context = Context(true);
        var finding = Assert.Single(context.Findings!);
        var suggestion = Assert.Single(RenderCoachingAdvice.Render(context,
            [new(finding.Id, finding.ExerciseIds.Single())]).Suggestions);
        Assert.Contains("05:15–05:45", suggestion.Interpretation);
        Assert.Contains("05:30", suggestion.Interpretation);
        var episode = Assert.Single(suggestion.Episodes!);
        Assert.Equal(new CoachingEconomyInterval(300, 360, -50, 2), episode.Economy);
    }

    [Fact]
    public void UnknownIsolationCannotBePresentedAsAnIsolatedDeathEpisode()
    {
        var context = Context(null, 0);
        var finding = Assert.Single(context.Findings!);
        Assert.Empty(finding.EpisodeIds!);
        var suggestion = Assert.Single(RenderCoachingAdvice.Render(context,
            [new(finding.Id, finding.ExerciseIds.Single())]).Suggestions);
        Assert.Empty(suggestion.Episodes!);
        Assert.Contains("apenas o resumo agregado", suggestion.Interpretation);
    }

    [Fact]
    public void ForgedCoverageOrReferencesAreRejected()
    {
        var context = Context(true, 1);
        Assert.NotEmpty(CoachingFindingSelector.ValidateChoices(context, []));
        context = Context(true);
        var finding = context.Findings![0];
        var forged = context with { Findings = [finding with { EpisodeIds = ["other-death"] }] };
        Assert.Throws<InvalidDataException>(() => RenderCoachingAdvice.Render(forged,
            [new(finding.Id, finding.ExerciseIds.Single())]));
    }

    [Fact]
    public void EpisodesOutsideTheMatchOrForAnotherHeroAreRejected()
    {
        var context = Context(true);
        var episode = context.Episodes![0];
        Assert.NotEmpty(CoachingFindingSelector.ValidateChoices(context with { Episodes = [episode with { HeroId = 2 }] }, []));
        Assert.NotEmpty(CoachingFindingSelector.ValidateChoices(context with { Episodes = [episode with { ToSeconds = 601 }] }, []));
    }
}

using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Coaching;

namespace DotaAnalyst.Tests.Application;

public class CoachingFindingsRegressionTests
{
    private static CoachingContext Context(int role = 3, double nearby = 1)
    {
        var context = new CoachingContext(2, "1", 38, new(role, "inferred", true), new(null, "unknown", null, false),
            1200, "omniscient-retrospective",
            [new("deaths", "Deaths", 5, "count", 0, 1200, "replay", ""),
             new("isolated", "IsolatedDeaths", 4, "count", 0, 1200, "replay", ""),
             new("fights", "TeamFights", 6, "count", 0, 1200, "replay", ""),
             new("nearby", "FightsNearby", nearby, "count", 0, 1200, "replay", ""),
             new("assists", "FinalAssists", 3, "Count", 0, 1200, "replay", ""),
             new("farm", "NetWorthAt10", 4127, "gold", 600, 600, "replay", "No baseline.")], []);
        return context with { Findings = CoachingFindingSelector.Select(context) };
    }

    [Fact]
    public void TotalFightsRemainDistinctFromProximityAndUnusedAssistsCannotBecomeAClaim()
    {
        var context = Context();
        var finding = context.Findings!.Single(f => f.Kind == CoachingFindingKind.FightProximityReview);
        Assert.Equal(new[] { "fights", "nearby" }, finding.EvidenceIds);
        var advice = RenderCoachingAdvice.Render(context, [new(finding.Id, finding.ExerciseIds.Single())]);
        var text = Assert.Single(advice.Suggestions);
        Assert.Contains("6 lutas na partida", text.Interpretation);
        Assert.Contains("amostrado de 1", text.Interpretation);
        Assert.DoesNotContain("assists", text.EvidenceIds);
        Assert.Equal(3, context.Role.Position);
        Assert.All(context.Findings!, f => Assert.Equal(3, f.Role));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ExercisesCannotBeBorrowedFromAnotherRole(int role)
    {
        var context = Context(role);
        var finding = context.Findings![0];
        var otherRole = role == 5 ? 1 : 5;
        Assert.Throws<InvalidDataException>(() => RenderCoachingAdvice.Render(context,
            [new(finding.Id, $"{finding.Id}-role-{otherRole}")]));
    }

    [Fact]
    public void FarmWithoutBaselineAndAssistsDoNotProduceJudgmentsOrRoleReclassification()
    {
        var context = Context(5);
        Assert.All(context.Findings!, f => Assert.DoesNotContain("farm", f.EvidenceIds));
        Assert.All(context.Findings!, f => Assert.DoesNotContain("assists", f.EvidenceIds));
        Assert.Equal(5, context.Role.Position);
        var finding = context.Findings!.Single(f => f.Kind == CoachingFindingKind.IsolatedDeathsReview);
        var advice = RenderCoachingAdvice.Render(context, [new(finding.Id, finding.ExerciseIds.Single())]);
        var text = Assert.Single(advice.Suggestions);
        Assert.Contains("não comprova", text.Interpretation);
        Assert.Contains("inferida", text.Interpretation);
    }

    [Fact]
    public void ImpossibleCountsAndPartialWindowsCannotBecomeFindings()
    {
        var context = Context(3, nearby: 7);
        Assert.DoesNotContain(context.Findings!, f => f.Kind == CoachingFindingKind.FightProximityReview);
        var partial = context with { Evidence = context.Evidence.Select(e => e with { ToSeconds = 600 }).ToArray() };
        Assert.Empty(CoachingFindingSelector.Select(partial));
    }

    [Fact]
    public void StaleCandidatesAndDuplicatesAreRejected()
    {
        var context = Context();
        var finding = context.Findings![0];
        var choice = new CoachingChoice(finding.Id, finding.ExerciseIds.Single());
        Assert.Throws<InvalidDataException>(() => RenderCoachingAdvice.Render(context, [choice, choice]));
        var stale = context with { Evidence = [] };
        Assert.Throws<InvalidDataException>(() => RenderCoachingAdvice.Render(stale, [choice]));
        Assert.Empty(RenderCoachingAdvice.Render(context with { Evidence = [], Findings = [] }, []).Suggestions);
    }
}

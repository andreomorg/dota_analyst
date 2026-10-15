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
}

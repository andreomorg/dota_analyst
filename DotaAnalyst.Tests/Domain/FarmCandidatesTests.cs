using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class FarmCandidatesTests
{
    private static FarmResource Unit(string id, int team, bool? seen) => new(id, "creep", team, 300, 0, 100, 30, 40, seen, false);
    private static readonly HeroPositions[] Positions = [new(1, [new(10, 0, 0)]), new(2, [new(10, 290, 0)])];
    private static readonly Dictionary<int, Team> Teams = new() { [1] = Team.Radiant, [2] = Team.Radiant };
    [Fact]
    public void EnumeratesOnlyKnownEnemyOrWildResourcesWithRealBaseRangeAndCompetitionEvidence()
    {
        var result = FarmCandidates.At(1, Team.Radiant, 10,
            [new(10, [Unit("own", 2, true), Unit("enemy", 3, true), Unit("wild", 4, true), Unit("fog", 3, false), Unit("unknown", 4, null)])], Positions, Teams);
        Assert.True(result.Available);
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, c => { Assert.Equal(300, c.StraightLineDistance); Assert.True(c.AllyCloser); Assert.Null(c.AccessEstablished); Assert.Null(c.ClearSeconds); });
    }
    [Fact]
    public void DoesNotCarryResourcesForwardOrInferAliveHeroFromMissingPosition()
    {
        var snapshots = new FarmResourceSnapshot[] { new(10, [Unit("old", 3, true)]) };
        Assert.False(FarmCandidates.At(1, Team.Radiant, 11, snapshots, Positions, Teams).Available);
        Assert.False(FarmCandidates.At(1, Team.Radiant, 10, snapshots, [], Teams).Available);
        Assert.False(FarmCandidates.At(1, Team.Radiant, 10, null, Positions, Teams).Available);
        Assert.Empty(FarmCandidates.At(1, Team.Radiant, 10, [new(10, [])], Positions, Teams).Candidates);
    }
}

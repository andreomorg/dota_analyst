using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;
public class DeathGoldAccountingTests
{
    [Fact]
    public void CounterExplainsCappedNominalLogWithoutClaimingLogIsEffectiveLoss()
    {
        var result = DeathGoldAccounting.Window(1, 0, 20,
            [new(1, 10, 100, 140, 40, 0, 9.9)], [new(1, 10, -100, 1, null, null)]);
        Assert.Equal(40, result.CounterLoss);
        var row = Assert.Single(result.Changes);
        Assert.Equal(100, row.NominalLogLoss);
        Assert.True(row.ConsistentWithUnreliableCap);
        Assert.Equal(0, result.UnassociatedLossLogs);
    }
    [Fact]
    public void AmbiguousOrFutureLogDoesNotBecomeAnExplanation()
    {
        var changes = new GoldLossObservation[] { new(1, 10, 0, 40, 40, 0, 9.9) };
        var result = DeathGoldAccounting.Window(1, 0, 10, changes, [new(1, 10.1, -100, 1, null, null)]);
        Assert.Null(Assert.Single(result.Changes).NominalLogLoss);
        result = DeathGoldAccounting.Window(1, 0, 20, changes,
            [new(1, 10, -100, 1, null, null), new(1, 10.1, -100, 1, null, null)]);
        Assert.Null(Assert.Single(result.Changes).ConsistentWithUnreliableCap);
        Assert.Equal(2, result.UnassociatedLossLogs);
    }
    [Fact]
    public void MissingAndKnownEmptyCountersAreDifferent()
    {
        Assert.Null(DeathGoldAccounting.Window(1, 0, 10, null, []).CounterLoss);
        Assert.Equal(0, DeathGoldAccounting.Window(1, 0, 10, [], []).CounterLoss);
    }
}

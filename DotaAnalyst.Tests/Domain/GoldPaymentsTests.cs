using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class GoldPaymentsTests
{
    [Fact]
    public void SameReasonKeepsBothSigns_UnknownReasonsAndFractionalBoundary()
    {
        var result = GoldPayments.Window(1, 1, 2.5,
            [new(1, 1, 100, 6, null, null), new(1, 2, -40, 6, null, null), new(1, 2.5, 20, null, null, null),
             new(1, 2.6, 999, 6, null, null), new(2, 2, 999, 6, null, null)]);
        var row = Assert.Single(result.Reasons, r => r.RawReason == 6);
        Assert.Equal(2, row.Observations);
        Assert.Equal(new GoldChanges(100, 40, 60), row.Changes);
        Assert.Equal(2, result.Reasons.Count);
        Assert.False(result.CompleteLedgerEstablished);
    }

    [Fact]
    public void MissingAndEmptyRemainDifferent()
    {
        Assert.False(GoldPayments.Window(1, 0, 10, null).Available);
        Assert.True(GoldPayments.Window(1, 0, 10, []).Available);
        Assert.Empty(GoldPayments.Window(1, 0, 10, []).Reasons);
    }
}

using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class BuybackReadinessTests
{
    [Fact]
    public void RecentCooldownDisprovesReadiness_ExpiredStaleFutureAndUnknownPriceRemainUnknown()
    {
        var rows = new EconomyObservation[] { new(1, 10, 13000, 3000, 4000, 5, 0) };
        Assert.False(BuybackReadiness.At(1, 10, rows));
        Assert.Null(BuybackReadiness.At(1, 12, rows));
        Assert.Null(BuybackReadiness.At(1, 13, rows));
        Assert.Null(BuybackReadiness.At(1, 9, rows));
        Assert.Null(BuybackReadiness.At(1, 10, [rows[0] with { CooldownRemaining = 0 }]));
        Assert.Null(BuybackReadiness.At(1, 10, null));
    }
}

using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class BuybackEconomyTests
{
    [Fact]
    public void ObservedCostDoesNotBecomeFormulaOrMissingZero()
    {
        Assert.Null(BuybackEconomy.Window(1, 0, 10, null).RecordedCostTotal);
        Assert.Equal(0, BuybackEconomy.Window(1, 0, 10, []).RecordedCostTotal);
        var uses = new BuybackUse[] { new(1, 2.5, 637), new(1, 9, null), new(2, 3, 999), new(1, 10.1, 999) };
        Assert.Equal(637, BuybackEconomy.Window(1, 0, 8, uses).RecordedCostTotal);
        Assert.Null(BuybackEconomy.Window(1, 0, 10, uses).RecordedCostTotal);
        Assert.Equal(2, BuybackEconomy.Window(1, 0, 10, uses).Uses.Count);
    }
}

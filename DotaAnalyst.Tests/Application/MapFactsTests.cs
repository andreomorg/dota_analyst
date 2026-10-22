using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public class MapFactsTests
{
    private static readonly ReplayAdditionalFacts Facts = new()
    {
        TempTrees =
        [
            new TempTreeChange(90, 10, 10, true), new TempTreeChange(95, 10, 10, false),   // gone before the window
            new TempTreeChange(102, 20, 20, true),                                          // stands at 110
            new TempTreeChange(105, 30, 30, true), new TempTreeChange(120, 30, 30, false),  // goes away after 110
        ],
        BossTracks =
        [
            new BossTrack("roshan", [new BossSample(0, -3194, 2395, true), new BossSample(100, -3000, 2000, true), new BossSample(200, 0, 0, false)]),
            new BossTrack("tormentor", [new BossSample(1200, -7680, 6336, true)]),          // not seen yet at 110
        ],
        Lanterns = [new LanternUse(1, 104, 500, 600), new LanternUse(2, 104, null, null), new LanternUse(1, 300, null, null)],
        GoldEvents =
        [
            new GoldEvent(1, 101, 120, 13, 100, 100), new GoldEvent(1, 103, 40, 13, null, null),
            new GoldEvent(1, 106, -300, 1, 0, 0), new GoldEvent(1, 140, 999, 13, 0, 0),
        ],
    };

    [Fact]
    public void Window_StateAtTheAnchor_NeverLater()
    {
        var window = MapFacts.Window(Facts, 1, 100, 110);

        Assert.Equal(2, window.TempTreesStanding);
        Assert.Equal(2, window.TempTreesAppeared);
        var roshan = Assert.Single(window.BossesAtAnchor!);
        Assert.Equal(("roshan", 100, true), (roshan.Kind, roshan.ObservedAt, roshan.Alive));
        Assert.Equal(1, window.LanternsActivated);
        Assert.Equal((160L, 300L), (window.GoldGained!.Value, window.GoldLost!.Value));
        Assert.Equal(160, window.GoldByReason!["13"]);
        Assert.Equal(1, window.GoldEventsWithoutLocation);
    }

    [Fact]
    public void Window_DocumentsBeforeMp3_AreUnknown()
    {
        var window = MapFacts.Window(new ReplayAdditionalFacts(), 1, 0, 60);

        Assert.Equal(FactAvailability.Unavailable, window.TempTrees);
        Assert.Null(window.TempTreesStanding);
        Assert.Null(window.BossesAtAnchor);
        Assert.Null(window.GoldGained);
    }
}

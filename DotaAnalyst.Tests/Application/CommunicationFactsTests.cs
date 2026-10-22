using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public class CommunicationFactsTests
{
    [Fact]
    public void Window_CountsPingsPhrasesAndCastOrigins()
    {
        var activity = new ReplayActivity([], [],
            [
                new RecordedUse(1, 100, "pudge_meat_hook", TargetX: 10, TargetY: 20, PointSource: 1),     // order point
                new RecordedUse(1, 105, "pudge_dismember", TargetHeroId: 6),
                new RecordedUse(1, 107, "lina_light_strike_array", TargetX: 1, TargetY: 2, PointSource: 0), // log location
                new RecordedUse(1, 108, "pudge_rot"),                                                        // no target
                new RecordedUse(1, 108.5, "x", TargetX: 1, TargetY: 2, PointSource: 9),                      // unknown origin
                new RecordedUse(1, 130, "pudge_meat_hook", TargetX: 0, TargetY: 0, PointSource: 1),          // after the window
                new RecordedUse(2, 101, "other_hero_spell", TargetHeroId: 1),
            ],
            pings: [new MapPing(1, Team.Radiant, 101, 0, 0, 0), new MapPing(1, Team.Radiant, 102, 0, 0, 0), new MapPing(1, Team.Radiant, 103, 0, 0, 5)]);
        var facts = new ReplayAdditionalFacts { ChatWheel = [new ChatWheelUse(1, 104, 7), new ChatWheelUse(1, 99, 7), new ChatWheelUse(6, 104, 3)] };

        var window = CommunicationFacts.Window(activity, facts, 1, 100, 110);

        Assert.Equal(2, window.PingsByType![0]);
        Assert.Equal(1, window.PingsByType[5]);
        Assert.Equal(1, Assert.Single(window.PhrasesById!).Value);
        Assert.Equal(5, window.CastCount);
        Assert.Equal(1, window.CastsAtHero);
        Assert.Equal((1, 1, 1), (window.CastPointsFromCombatLog!.Value, window.CastPointsFromOrders!.Value, window.CastPointsOfUnknownOrigin!.Value));
    }

    [Fact]
    public void Window_MissingFamiliesAreUnknown()
    {
        var window = CommunicationFacts.Window(new ReplayActivity([], [], []), new ReplayAdditionalFacts(), 1, 0, 60);

        Assert.Equal(FactAvailability.Unavailable, window.Pings);
        Assert.Null(window.PingsByType);
        Assert.Equal(FactAvailability.Unavailable, window.ChatWheel);
        Assert.Equal(FactAvailability.Available, window.Casts);
        Assert.Equal(0, window.CastCount);
    }
}

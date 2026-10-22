using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

// ObjectivePlaces: which boss track death belongs to which kill, and the hero's presence with gaps and deaths.
public class ObjectivePlacesTests
{
    private static MapObjective Tormentor(int seconds) => new(ObjectiveKind.Tormentor, seconds, Team.Radiant, killerHeroId: 1);

    private static BossTrack Track(string kind, params (int Seconds, int X, int Y, bool Alive)[] samples) =>
        new(kind, samples.Select(sample => new BossSample(sample.Seconds, sample.X, sample.Y, sample.Alive)).ToList());

    [Fact]
    public void LateDeathSample_PlacesTheBossWhereItWasByTheKill()
    {
        var roshan = new MapObjective(ObjectiveKind.Roshan, 1000, Team.Radiant, killerHeroId: 1);
        // Walking between the pits, then killed at 1000; the dead sample only at 1060.
        var places = ObjectivePlaces.Locate([roshan], [Track("roshan", (900, 2900, -2700, true), (990, 100, 50, true), (1060, 100, 50, false))], null);

        Assert.Equal((100, 50), (places.Single().X, places.Single().Y));
    }

    [Fact]
    public void TwoTormentorsWithLateDeathsInEitherOrder_AreAmbiguous()
    {
        // A dies at 1000 (dead sample 1077), B at 1030 (dead sample 1032): either kill could be either death.
        IReadOnlyList<BossTrack> tracks =
        [
            Track("tormentor", (0, -7680, 6336, true), (1077, -7680, 6336, false)),
            Track("tormentor", (0, 7500, -6900, true), (1032, 7500, -6900, false)),
        ];

        Assert.Empty(ObjectivePlaces.Locate([Tormentor(1000), Tormentor(1030)], tracks, null));
    }

    [Fact]
    public void BossNotSeenAliveBeforeTheKill_HasNoPlace()
    {
        var places = ObjectivePlaces.Locate([Tormentor(1000)], [Track("tormentor", (1001, 0, 0, false))], null);

        Assert.Empty(places);
    }

    [Fact]
    public void Presence_DeadIsAbsent_GapIsUnknown_AndTheEarlierSampleWins()
    {
        var kill = Tormentor(600);
        IReadOnlyList<ObjectivePlace> places = [new(kill, 0, 0)];
        HeroPositions Positions(params (int Seconds, int X, int Y)[] samples) =>
            new(1, samples.Select(sample => new PositionSample(sample.Seconds, sample.X, sample.Y)).ToList());

        // Far at 599, next to it at 601 (a Blink just after): not there.
        var blink = MapObjectiveMetricsCalculator.Calculate(Team.Radiant, Positions((599, 5000, 5000), (601, 0, 0)), [kill], places, null);
        // Dead since 580: not there, and known.
        var dead = MapObjectiveMetricsCalculator.Calculate(Team.Radiant, Positions((578, 0, 0), (640, 0, 0)), [kill], places, null,
            heroDeaths: [new HeroDeath(1, 580, 0, 0, KillerKind.Hero, 6, false)]);
        // No sample around 600 and alive: unknown.
        var gap = MapObjectiveMetricsCalculator.Calculate(Team.Radiant, Positions((578, 0, 0), (640, 0, 0)), [kill], places, null);

        Assert.Equal(0, blink.TormentorKillsNearby);
        Assert.Equal(0, dead.TormentorKillsNearby);
        Assert.Null(gap.TormentorKillsNearby);
    }
}

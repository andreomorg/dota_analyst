using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Domain;

public class MapObjectiveTests
{
    [Fact]
    public void ValidObjectives_AreAccepted()
    {
        _ = new MapObjective(ObjectiveKind.Tower, 600, Team.Dire, 28, Team.Radiant, towerTier: 1, lane: Lane.Top);
        _ = new MapObjective(ObjectiveKind.Tower, 1800, Team.Dire, null, Team.Radiant, towerTier: 4);
        _ = new MapObjective(ObjectiveKind.Barracks, 1700, Team.Radiant, 31, Team.Dire, lane: Lane.Bottom, meleeBarracks: true);
        _ = new MapObjective(ObjectiveKind.Tower, 900, Team.Radiant, 31, Team.Radiant, towerTier: 2, lane: Lane.Middle, denied: true);
        _ = new MapObjective(ObjectiveKind.Roshan, 1300, Team.Radiant, 31, aegisHeroId: 5);
    }

    public static TheoryData<Func<MapObjective>> Invalid => new()
    {
        // A building without its owner, or Roshan with one.
        () => new MapObjective(ObjectiveKind.Ancient, 1900, Team.Dire, null),
        () => new MapObjective(ObjectiveKind.Roshan, 1300, Team.Radiant, null, owner: Team.Dire),
        // A tower without a tier, a tier 5, a tier 4 in a lane, a tier 1 without one.
        () => new MapObjective(ObjectiveKind.Tower, 600, Team.Dire, null, Team.Radiant, lane: Lane.Top),
        () => new MapObjective(ObjectiveKind.Tower, 600, Team.Dire, null, Team.Radiant, towerTier: 5, lane: Lane.Top),
        () => new MapObjective(ObjectiveKind.Tower, 600, Team.Dire, null, Team.Radiant, towerTier: 4, lane: Lane.Top),
        () => new MapObjective(ObjectiveKind.Tower, 600, Team.Dire, null, Team.Radiant, towerTier: 1),
        // Barracks in the jungle, or without melee/ranged.
        () => new MapObjective(ObjectiveKind.Barracks, 600, Team.Dire, null, Team.Radiant, lane: Lane.Jungle, meleeBarracks: true),
        () => new MapObjective(ObjectiveKind.Barracks, 600, Team.Dire, null, Team.Radiant, lane: Lane.Top),
        // Taken by its owner without a deny, or denied by the enemy.
        () => new MapObjective(ObjectiveKind.Ancient, 1900, Team.Radiant, null, Team.Radiant),
        () => new MapObjective(ObjectiveKind.Ancient, 1900, Team.Dire, null, Team.Radiant, denied: true),
        // An Aegis from a Tormentor.
        () => new MapObjective(ObjectiveKind.Tormentor, 1300, Team.Radiant, 31, aegisHeroId: 31),
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidObjectives_AreRefused(Func<MapObjective> create)
    {
        Assert.ThrowsAny<ArgumentException>(() => create());
    }
}

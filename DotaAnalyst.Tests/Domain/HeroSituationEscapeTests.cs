using System.Text.Json;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Maps;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Situations;

namespace DotaAnalyst.Tests.Domain;

public class HeroSituationEscapeTests
{
    private static HeroDetails VisionHero(double radius) => new(HeroAttribute.Strength, HeroAttackType.Melee, new HashSet<HeroRole>())
    {
        Profile = new HeroProfile("test", true, new(null, null, null, null, null, null),
            new(null, null, null, null, null, null, null, null, null), new(null, null), new(null, null, null, null),
            new(null, null, new(radius, HeroValueOrigin.HeroFile), new(radius, HeroValueOrigin.HeroFile))),
    };

    private static HeroSituationBuilder Builder(ReplayActivity? activity = null, GameMap? map = null,
        bool seenEnemy = true, double heroRadius = 256, bool futureOnly = false, IReadOnlyList<WardPlacement>? wards = null)
    {
        MatchId.TryCreate(1, out var id);
        LaningStats[] players = [new(1, Team.Radiant, Lane.Bottom, 0, 0, 0, 0, 0),
            new(2, Team.Radiant, Lane.Bottom, 0, 0, 0, 0, 0), new(6, Team.Dire, Lane.Top, 0, 0, 0, 0, 0)];
        var timeline = new MatchTimeline([0, 180], players.Select(p => new HeroTimeline(p.HeroId, p.Team, [0, 0], [0, 0])).ToArray());
        return HeroSituationBuilder.From(new ReplayAnalysis(id, 180, players, timeline, [], wards ?? [], objectives: []),
            [new(1, [new(futureOnly ? 101 : 100, 0, 0)]), new(2, [new(100, 768, 0), new(101, -256, 0)]),
                new(6, [new(100, 384, 100, new(100, 100, 0, 0, seenEnemy)), new(101, 0, 0, new(100, 100, 0, 0, true))])],
            activity, new GameCatalog(Heroes: new Dictionary<int, HeroDetails> { [1] = VisionHero(heroRadius), [2] = VisionHero(0) }, Map: map))!;
    }

    private static ReplayActivity Activity() => new([], [], [], trees: new TreeTimeline([0], []),
        dayNight: [new(0, true), new(101, false)], visionUnits: [new(100, [], []), new(101, [new(0, 0, 5000, 5000)], [])],
        buildings: [new("tower", Team.Radiant, BuildingKind.Tower, Lane.Bottom, 1, 768, 0, [new(0, 100, 100), new(101, 0, 100)])]);

    [Fact]
    public void HandComputedRouteVisionAndEnemyDistance_AtIsUnchanged()
    {
        var builder = Builder(Activity(), MapPathfinderTests.Map());
        var before = JsonSerializer.Serialize(builder.At(1, 100));
        var escape = builder.EscapeAt(1, 100)!;
        var route = escape.ToNearestAlly!;
        Assert.Equal(new MapPoint(768, 0), route.Destination);
        Assert.Equal(768, route.PathLength);
        Assert.Equal(768, route.StraightDistance);
        Assert.Equal(1, route.Detour);
        // x=0,256 are seen by self, x=768 by ally; x=512 is unseen.
        Assert.Equal(0.75, route.SeenShare);
        Assert.Equal(100, route.NearestSeenEnemyToPath);
        Assert.Equal(100, route.HiddenNearestEnemyToPath);
        Assert.Equal(route, escape.ToOwnTowerOrFountain);
        Assert.Equal(before, JsonSerializer.Serialize(builder.At(1, 100)));
    }

    [Fact]
    public void HiddenEnemyIsOnlyInHiddenMeasurement_UnknownSourcesStayNull()
    {
        var builder = Builder(Activity(), MapPathfinderTests.Map(), seenEnemy: false);
        Assert.Null(builder.EscapeAt(1, 100)!.ToNearestAlly!.NearestSeenEnemyToPath);
        Assert.Equal(100, builder.EscapeAt(1, 100)!.ToNearestAlly!.HiddenNearestEnemyToPath);
        var noFrame = new ReplayActivity([], [], [], trees: new TreeTimeline([0], []));
        Assert.Null(Builder(noFrame, MapPathfinderTests.Map()).EscapeAt(1, 100)!.ToNearestAlly!.SeenShare);
    }

    [Fact]
    public void MobileAndObserverSourcesAreIncluded()
    {
        var moving = new ReplayActivity([], [], [], trees: new TreeTimeline([0], []), dayNight: [new(0, true)],
            visionUnits: [new(100, [new(384, 0, 1000, 1000)], [])]);
        Assert.Equal(1, Builder(moving, MapPathfinderTests.Map(), heroRadius: 0).EscapeAt(1, 100)!.ToNearestAlly!.SeenShare);
        WardPlacement ward = new(WardKind.Observer, Team.Radiant, 1, 384, 0, 99, 101, false, null);
        Assert.Equal(1, Builder(Activity(), MapPathfinderTests.Map(), heroRadius: 0, wards: [ward]).EscapeAt(1, 100)!.ToNearestAlly!.SeenShare);
    }

    [Fact]
    public void MissingMapOrTreesAreUnknown_DeadOrFutureHeroIsNull()
    {
        Assert.Equal(new SituationEscape(), Builder(Activity()).EscapeAt(1, 100));
        Assert.Equal(new SituationEscape(), Builder(map: MapPathfinderTests.Map()).EscapeAt(1, 100));
        Assert.Equal(new SituationEscape(), Builder(Activity(), MapPathfinderTests.Map(trees: false)).EscapeAt(1, 100));
        Assert.Null(Builder(Activity(), MapPathfinderTests.Map(), futureOnly: true).EscapeAt(1, 100));
        Assert.Null(Builder(Activity(), MapPathfinderTests.Map()).EscapeAt(1, 102));
        Assert.Null(Builder().EscapeAt(99, 100));
    }

    [Fact]
    public void TreeChangesOnlyApplyAtTheirSecond()
    {
        var activity = new ReplayActivity([], [], [], trees: new TreeTimeline([], [new(101, 0, false)]),
            buildings: [new("tower", Team.Radiant, BuildingKind.Tower, Lane.Bottom, 1, 768, 0, [new(0, 100, 100)])]);
        var builder = Builder(activity, MapPathfinderTests.Map());
        Assert.True(builder.EscapeAt(1, 100)!.ToNearestAlly!.PathLength > 768);
        // Asking for a later anchor cannot mutate the earlier result's tree state.
        Assert.Equal(768, builder.EscapeAt(1, 101)!.ToOwnTowerOrFountain!.PathLength);
        Assert.True(builder.EscapeAt(1, 100)!.ToNearestAlly!.PathLength > 768);
        var trees = activity.Trees!;
        Assert.DoesNotContain(0, trees.DownAt(100));
        Assert.Contains(0, trees.DownAt(101));
    }

    [Fact]
    public void FountainFallbackRequiresKnownBuildingState()
    {
        var original = MapPathfinderTests.Map();
        var map = new GameMap(original.Patch, original.Grid, original.Trees, [], [], [], [], [], [],
            [new(LandmarkKind.Fountain, "fountain", Team.Radiant, -256, 0)]);
        var fallen = new ReplayActivity([], [], [], trees: new TreeTimeline([0], []),
            buildings: [new("tower", Team.Radiant, BuildingKind.Tower, Lane.Bottom, 1, 768, 0,
                [new(0, 0, 100), new(101, 100, 100)])]);
        Assert.Equal(new MapPoint(-256, 0), Builder(fallen, map).EscapeAt(1, 100)!.ToOwnTowerOrFountain!.Destination);
        var unread = new ReplayActivity([], [], [], trees: new TreeTimeline([0], []),
            buildings: [new("tower", Team.Radiant, BuildingKind.Tower, Lane.Bottom, 1, 768, 0, [new(101, 100, 100)])]);
        Assert.Null(Builder(unread, map).EscapeAt(1, 100)!.ToOwnTowerOrFountain);
    }

    [Fact]
    public void StaleMovingVisionAndFutureWardsDoNotCount()
    {
        var stale = new ReplayActivity([], [], [], trees: new TreeTimeline([0], []), dayNight: [new(0, true)],
            visionUnits: [new(98, [new(384, 0, 1000, 1000)], []), new(101, [], [])]);
        Assert.Null(Builder(stale, MapPathfinderTests.Map()).EscapeAt(1, 100)!.ToNearestAlly!.SeenShare);
        WardPlacement future = new(WardKind.Observer, Team.Radiant, 1, 384, 0, 101, null, false, null);
        Assert.Equal(0.5, Builder(Activity(), MapPathfinderTests.Map(), heroRadius: 0, wards: [future])
            .EscapeAt(1, 100)!.ToNearestAlly!.SeenShare);
    }
}

using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Heroes;
using DotaAnalyst.Domain.Items;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Tests.Application;

public sealed class GetSituatedDecisionsHandlerTests
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ModelSamplesKeepBothVersionedProductionPaths(int version)
    {
        var catalogs = new Catalogs();
        var handler = new GetModelSamplesHandler(
            new Archive(new(Replay(), [new(1, [new(90, 0, 0)])])),
            new GameCatalogLoader(catalogs, catalogs, catalogs, catalogs));
        var death = (await handler.DeathRiskAsync(Id, CancellationToken.None, version)).Samples!;
        var region = (await handler.NextRegionAsync(Id, CancellationToken.None, version)).Samples!;
        Assert.Equal(version, death.FeatureVersion);
        Assert.Equal(version, region.FeatureVersion);
        Assert.True(Assert.Single(death.Samples).DiesSoon);
        Assert.Single(region.Samples);
        Assert.Equal(Assert.Single(death.Samples).Situation, Assert.Single(region.Samples).Situation);
    }

    [Fact]
    public async Task SignalsAreOptInAndDoNotChangeLegacyScenes()
    {
        var handler = Handler(new(Replay(), [new(1, [new(90, 0, 0)])], StartedAt: DateTimeOffset.UnixEpoch));
        var legacy = (await handler.HandleAsync(Id, 1, CancellationToken.None))!;
        var current = (await handler.HandleAsync(Id, 1, CancellationToken.None, includeSignals: true))!;
        Assert.All(legacy.Situations, anchor => Assert.Null(anchor.Signals));
        Assert.All(current.Situations, anchor => Assert.NotNull(anchor.Signals));
        Assert.Equal(legacy.Situations, current.Situations.Select(anchor => anchor with { Signals = null }));
        var json = System.Text.Json.JsonSerializer.Serialize(legacy, WebJson);
        Assert.DoesNotContain("\"signals\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_HasNoHiddenFieldAndEveryFieldIsAModelColumn()
    {
        var columns = typeof(SituationRow).GetProperties().Select(property => property.Name).ToHashSet();
        var fields = typeof(CoachingSituationSnapshot).GetProperties().Select(property => property.Name).ToList();

        Assert.Equal(62, fields.Count);
        Assert.DoesNotContain(fields, field => field.StartsWith("Hidden", StringComparison.Ordinal));
        Assert.All(fields, field => Assert.Contains(field, columns));
    }

    [Fact]
    public async Task EachSceneGetsTheSituationAtItsAnchor_AndTheHeroesByCatalogName()
    {
        var handler = Handler(new(Replay(), [new(1, [new(88, 0, 0), new(90, 0, 0)]), new(2, [new(90, 3000, 3000)])],
            Activity: null, StartedAt: DateTimeOffset.UnixEpoch));

        var context = (await handler.HandleAsync(Id, 1, CancellationToken.None))!;

        Assert.Equal(5, context.SchemaVersion);
        var scene = Assert.Single(context.Decision.Scenes);
        var anchor = Assert.Single(context.Situations);
        Assert.Equal(scene.AtOrBeforeAnchor.AnchorSeconds, anchor.AnchorSeconds);
        Assert.NotNull(anchor.Situation);
        Assert.Equal((0, 1), (anchor.Situation.AlliesAlive, anchor.Situation.EnemiesAlive));
        // Without the replay's activity the recent damage is unknown, not zero.
        Assert.Null(anchor.Situation.DamageFromHeroesLast6Seconds);
        // No map in the fake catalog: both routes unknown, never invented.
        Assert.NotNull(anchor.Escape);
        Assert.Null(anchor.Escape.ToTower);
        Assert.Null(anchor.Escape.ToAlly);
        Assert.DoesNotContain(typeof(CoachingRouteSnapshot).GetProperties(), p => p.Name.Contains("Hidden", StringComparison.Ordinal));
        Assert.Equal(["Anti-Mage", "Unknown hero"], context.Heroes.Select(hero => hero.Name));
        Assert.Equal([HeroRole.Carry, HeroRole.Escape], context.Heroes[0].Roles);
    }

    [Fact]
    public async Task OfficialPatchAndKitEffects_ComeFromTheMatchBuild()
    {
        var catalogs = new Catalogs();
        var handler = new GetSituatedDecisionsHandler(
            new Archive(new(Replay(), [new(1, [new(90, 0, 0)])], StartedAt: DateTimeOffset.UnixEpoch.AddDays(2))),
            new GameCatalogLoader(catalogs, catalogs, catalogs, catalogs), catalogs, new NoMatches(),
            new Patches([new("7.41f", DateTimeOffset.UnixEpoch)]));

        var context = (await handler.HandleAsync(Id, 1, CancellationToken.None))!;

        Assert.Equal("7.41f", context.Decision.Patch.MatchPatch);
        Assert.Equal("game-build:build", context.Decision.Patch.Source);
        // The fake catalog has no kits: unknown, never "no effect".
        Assert.All(context.Heroes, hero => Assert.Null(hero.KitEffects));
    }

    [Fact]
    public async Task NoSampleAtTheAnchor_LeavesTheSituationNull()
    {
        var handler = Handler(new(Replay(), [new(1, [new(10, 0, 0)])], StartedAt: DateTimeOffset.UnixEpoch));

        var anchor = Assert.Single((await handler.HandleAsync(Id, 1, CancellationToken.None))!.Situations);

        Assert.Null(anchor.Situation);
    }

    [Fact]
    public async Task NoDocumentOrAnotherMatchsHero_IsNotFound()
    {
        Assert.Null(await Handler(null).HandleAsync(Id, 1, CancellationToken.None));
        Assert.Null(await Handler(new(Replay(), [])).HandleAsync(Id, 99, CancellationToken.None));
    }

    private static readonly MatchId Id = MatchId.TryCreate(1, out var id) ? id : default;

    private static GetSituatedDecisionsHandler Handler(ArchivedReplay? replay)
    {
        var catalogs = new Catalogs();
        return new(new Archive(replay), new GameCatalogLoader(catalogs, catalogs, catalogs, catalogs), catalogs, new NoMatches());
    }

    // Hero 1 dies at 100 s: one death episode, its anchor ten seconds before.
    private static ReplayAnalysis Replay() => new(Id, 120,
        [new(1, Team.Radiant, Lane.Middle, 0, 0, 0, 0, 0), new(2, Team.Dire, Lane.Middle, 0, 0, 0, 0, 0)],
        new MatchTimeline([0, 120], [new HeroTimeline(1, Team.Radiant, [600, 900], [0, 300]),
            new HeroTimeline(2, Team.Dire, [600, 900], [0, 300])]),
        [new HeroDeath(1, 100, 0, 0, KillerKind.Hero, 2, false)], wards: [], objectives: [],
        metrics: [new HeroMetrics(1, Team.Radiant, 2, 1, 0, 0, 0, 0, null, null, 0, 0, 0, 0, null, 0, 0)]);

    private sealed class Archive(ArchivedReplay? replay) : IArchivedReplaySource
    {
        public Task<ArchivedReplay?> FindAsync(MatchId id, CancellationToken ct) => Task.FromResult(replay);
    }

    private sealed class Patches(IReadOnlyList<GamePatch> patches) : IGamePatchCatalog
    {
        public Task<IReadOnlyList<GamePatch>> GetPatchesAsync(CancellationToken cancellationToken) => Task.FromResult(patches);
    }

    private sealed class NoMatches : IMatchRepository
    {
        public Task<SavedMatch?> FindAsync(MatchId matchId, CancellationToken cancellationToken) => Task.FromResult<SavedMatch?>(null);
        public Task SaveAsync(MatchDetails match, DateTimeOffset savedAt, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Catalogs : IHeroCatalog, IItemCatalog, IAbilityCatalog, IGameBuildCatalog
    {
        public Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<int, Hero>>(new Dictionary<int, Hero>
            {
                [1] = new(1, "Anti-Mage")
                {
                    Details = new HeroDetails(HeroAttribute.Agility, HeroAttackType.Melee, new HashSet<HeroRole> { HeroRole.Escape, HeroRole.Carry }),
                },
            });
        public Task<IReadOnlyDictionary<int, Item>> GetItemsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<int, Item>>(new Dictionary<int, Item>());
        public Task<IReadOnlyDictionary<string, AbilityDetails>> GetAbilitiesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, AbilityDetails>>(new Dictionary<string, AbilityDetails>());
        public Task<IReadOnlyDictionary<int, HeroKit>> GetKitsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<int, HeroKit>>(new Dictionary<int, HeroKit>());
        public Task<string?> GetPatchAsync(CancellationToken ct) => Task.FromResult<string?>(null);
        public Task<(GameBuild Build, GameCatalog Catalog)?> ForMatchAsync(DateTimeOffset? at, CancellationToken ct) =>
            Task.FromResult<(GameBuild, GameCatalog)?>((new("build", 6962, DateTimeOffset.UnixEpoch), GameCatalog.Empty));
    }
}

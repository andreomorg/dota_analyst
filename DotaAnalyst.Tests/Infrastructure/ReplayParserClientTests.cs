using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Infrastructure.ReplayParser;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

// The client against a fake parser (no network): the contract of replay-parser/CLAUDE.md, and above all that
// only the parser's own problem+json errors say something about the replay. ngrok answering for a machine that
// is off (even with a 404) must never become "replay expired".
public class ReplayParserClientTests
{
    private const string ApiKey = "secret-key";
    private const long MatchIdValue = 9034320024;
    private static readonly MatchId Match = ReplayBuilder.MatchIdOf(MatchIdValue);
    private static readonly Uri ReplayUrl = new("http://replay202.valve.net/570/9034320024_96247495.dem.bz2");

    // A /v1/match document (only the parts the API reads, plus one it ignores) for a 11:30 match: 12 points per
    // series (minutes 0..10, then the end); the values at minute 10 are the laning numbers.
    private static string MatchJson(string? laning = null, int schemaVersion = 1) => $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "match": { "matchId": "9034320024", "durationSeconds": 690.4 },
          "players": [
            { "player": 0, "heroId": 31, "team": "radiant", "name": "me" },
            { "player": 5, "heroId": 28, "team": "dire" }
          ],
          "timelines": { "perMinute": [
            { "player": 0, "netWorth": [600,700,800,900,1000,1200,1400,1600,1800,2000,2900,3000],
              "xp": [0,100,200,300,400,500,600,700,800,900,3100,3300], "lastHits": [0,1,2,3,4,5,6,7,8,9,12,13],
              "denies": [0,0,0,0,1,1,1,2,2,2,3,3], "deaths": [0,0,0,0,0,0,1,1,1,1,1,1] },
            { "player": 5, "netWorth": [600,800,1000,1200,1400,1600,1800,2000,2200,2400,3500,3600],
              "xp": [0,150,300,450,600,750,900,1050,1200,1350,2800,2900], "lastHits": [0,3,6,9,12,15,18,21,24,27,40,41],
              "denies": [0,0,0,0,0,0,0,0,0,0,0,0], "deaths": [0,0,0,0,0,0,0,0,0,0,0,0] }
          ], "positionIntervalSeconds": 2, "positions": [] },
          "laning": {{laning ?? """{ "endsAtSeconds": 600, "lanes": [ { "player": 0, "lane": "bottom" }, { "player": 5, "lane": "jungle" } ] }"""}},
          "kills": [],
          "vision": { "wards": [] },
          "combat": { "damage": [], "healing": [], "perMinute": [ { "player": 0, "damageToHeroes": [0,0,0,0,0,0,0,0,0,0,0,0], "damageFromHeroes": [0,0,0,0,0,0,0,0,0,0,0,0], "damageTaken": [], "healing": [] }, { "player": 5, "damageToHeroes": [0,0,0,0,0,0,0,0,0,0,0,0], "damageFromHeroes": [0,0,0,0,0,0,0,0,0,0,0,0], "damageTaken": [], "healing": [] } ] },
          "items": { "purchases": [], "uses": [], "final": [] },
          "objectives": { "buildings": [], "roshan": [], "tormentors": [], "couriers": [], "runes": [], "outposts": [] }
        }
        """;

    [Fact]
    public async Task Parse_SendsTheContractRequest_AndMapsTheAnswer()
    {
        var parser = new FakeParser(Json(HttpStatusCode.OK, MatchJson(), "application/json"));
        var archive = new FakeArchive();

        var analysis = await CreateClient(parser, archive: archive).ParseAsync(Match, ReplayUrl, CancellationToken.None);

        // Request: POST /v1/match with the token, ngrok's header and the replay address.
        var request = Assert.Single(parser.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://parser.example/v1/match", request.Uri);
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);
        Assert.Equal("1", request.NgrokSkipBrowserWarning);
        Assert.Equal(ReplayUrl.AbsoluteUri, JsonDocument.Parse(request.Body).RootElement.GetProperty("replayUrl").GetString());

        // Answer -> domain: the laning numbers are the minute-10 point.
        Assert.Equal(Match, analysis.MatchId);
        Assert.Equal(600, analysis.EndsAtSeconds);
        Assert.Equal(new LaningStats(31, Team.Radiant, Lane.Bottom, 12, 3, 2900, 3100, 1), analysis.FindHero(31));
        Assert.Equal(new LaningStats(28, Team.Dire, Lane.Jungle, 40, 0, 3500, 2800, 0), analysis.FindHero(28));

        // The timeline: a point per minute, the last one at the end of the match.
        var timeline = analysis.Timeline;
        Assert.NotNull(timeline);
        Assert.Equal([0, 60, 120, 180, 240, 300, 360, 420, 480, 540, 600, 690], timeline.Seconds);
        Assert.Equal(3000, timeline.FindHero(31)!.NetWorth[^1]);
        Assert.Equal(2900, timeline.FindHero(28)!.Experience[^1]);

        // The whole document is kept as it came.
        var kept = Assert.Single(archive.Kept);
        Assert.Equal((MatchIdValue, 1), (kept.MatchId, kept.SchemaVersion));
        Assert.Contains("\"kills\": []", kept.Document);
    }

    [Fact]
    public async Task Parse_Kills_BecomeDeathsWithTheKiller()
    {
        var kills = """
            "kills": [
              { "t": 312.4, "victim": 0, "killer": 5, "killerUnit": "npc_dota_hero_slardar", "x": 5100, "y": -6200, "reincarnated": false },
              { "t": 95.6, "victim": 5, "killer": null, "killerUnit": "npc_dota_goodguys_tower1_bot", "x": 6300, "y": -6000 },
              { "t": 700, "victim": 0, "killer": null, "killerUnit": "npc_dota_creep_badguys_melee", "x": 0, "y": 0, "reincarnated": true }
            ]
            """;

        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, MatchJson().Replace("\"kills\": []", kills), "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(
            [
                new HeroDeath(28, 96, 6300, -6000, KillerKind.Tower, null, reincarnated: false),
                new HeroDeath(31, 312, 5100, -6200, KillerKind.Hero, 28, reincarnated: false),
                new HeroDeath(31, 700, 0, 0, KillerKind.Creeps, null, reincarnated: true),
            ],
            analysis.Deaths);
    }

    [Fact]
    public async Task Parse_Wards_BecomeWardPlacements()
    {
        var wards = """
            "vision": { "wards": [
              { "type": "sentry", "player": 5, "team": "dire", "x": 100, "y": 200, "placedAt": 400.2, "removedAt": 461.0,
                "destroyed": true, "destroyedBy": 0, "destroyedByTeam": "radiant" },
              { "type": "observer", "player": null, "team": "radiant", "x": -3000, "y": 1500, "placedAt": -60.4, "removedAt": null,
                "destroyed": false, "destroyedBy": null, "destroyedByTeam": null }
            ] }
            """;
        var body = MatchJson().Replace("\"vision\": { \"wards\": [] }", wards);

        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(
            [
                new WardPlacement(WardKind.Observer, Team.Radiant, null, -3000, 1500, -60, null, destroyed: false, null),
                new WardPlacement(WardKind.Sentry, Team.Dire, 28, 100, 200, 400, 461, destroyed: true, 31),
            ],
            analysis.Wards);
    }

    [Fact]
    public async Task Parse_CombatDamage_KeepsRowsWithAHero_AndKillsKeepTheirAssists()
    {
        var body = MatchJson()
            .Replace("\"kills\": []", """
                "kills": [ { "t": 300, "victim": 5, "killer": null, "killerUnit": "npc_dota_goodguys_tower1_bot",
                  "x": 0, "y": 0, "assists": [0] } ]
                """)
            .Replace("\"combat\": { \"damage\": []", """
                "combat": { "damage": [
                  { "sourcePlayer": 0, "sourceKind": "illusion", "targetPlayer": 5, "targetKind": "hero",
                    "inflictor": null, "damageType": "physical", "amount": 450 },
                  { "sourcePlayer": null, "sourceKind": "buildings", "targetPlayer": 0, "targetKind": "hero",
                    "inflictor": null, "damageType": "physical", "amount": 300 },
                  { "sourcePlayer": null, "sourceKind": "laneCreeps", "targetPlayer": null, "targetKind": "buildings",
                    "inflictor": null, "damageType": "physical", "amount": 9000 }
                ]
                """);

        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(
            [
                new DamageRow(31, "illusion", 28, DamageRow.HeroKind, null, 450),
                new DamageRow(null, "buildings", 31, DamageRow.HeroKind, null, 300),
            ],
            analysis.Damage);
        Assert.Equal([31], Assert.Single(analysis.Deaths!).AssistHeroIds);
    }

    [Fact]
    public async Task Parse_Purchases_BecomeItemPurchasesWithoutThePrefix()
    {
        var body = MatchJson().Replace("\"purchases\": []", """
            "purchases": [
              { "player": 0, "items": [ [-89.9, "item_tango"], [1120.4, "item_recipe_black_king_bar"] ] },
              { "player": 5, "items": [ [34.6, "item_circlet"] ] }
            ]
            """);

        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(
            [
                new ItemPurchase(31, -90, "tango"),
                new ItemPurchase(28, 35, "circlet"),
                new ItemPurchase(31, 1120, "recipe_black_king_bar"),
            ],
            analysis.Purchases);
    }

    [Theory]
    [InlineData("""[ "item_tango", -89.9 ]""")]
    [InlineData("""[ -89.9 ]""")]
    [InlineData("""{ "t": -89.9, "name": "item_tango" }""")]
    public async Task Parse_PurchaseNotATuple_IsAnUnreadableReplay(string tuple)
    {
        var body = MatchJson().Replace("\"purchases\": []", $$"""
            "purchases": [ { "player": 0, "items": [ {{tuple}} ] } ]
            """);

        await Assert.ThrowsAsync<ReplayUnreadableException>(() =>
            CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json"))).ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_Objectives_BuildingsByName_RoshanAndTormentor()
    {
        // Building names as in match 9040879414; fillers and unknown names are skipped.
        var body = MatchJson().Replace(
            "\"objectives\": { \"buildings\": [], \"roshan\": [], \"tormentors\": []",
            """
            "objectives": {
              "buildings": [
                { "t": 679.6, "building": "npc_dota_goodguys_tower1_bot", "team": "radiant", "killer": 5, "killerUnit": "npc_dota_beastmaster_boar", "denied": false },
                { "t": 1009.9, "building": "npc_dota_badguys_tower2_top", "team": "dire", "killer": null, "killerUnit": "npc_dota_creep_goodguys_melee", "denied": false },
                { "t": 1100, "building": "npc_dota_badguys_tower3_mid", "team": "dire", "killer": 5, "denied": true },
                { "t": 1668.3, "building": "npc_dota_goodguys_range_rax_bot", "team": "radiant", "killer": 5, "denied": false },
                { "t": 1694.6, "building": "npc_dota_goodguys_melee_rax_mid", "team": "radiant", "killer": 5, "denied": false },
                { "t": 1696.9, "building": "npc_dota_goodguys_fillers", "team": "radiant", "killer": 5, "denied": false },
                { "t": 1700, "building": "npc_dota_goodguys_statue_of_a_new_patch", "team": "radiant", "killer": 5, "denied": false },
                { "t": 1888.4, "building": "npc_dota_goodguys_tower4", "team": "radiant", "killer": 5, "denied": false },
                { "t": 1905.9, "building": "npc_dota_goodguys_fort", "team": "radiant", "killer": 5, "denied": false }
              ],
              "roshan": [ { "t": 1300.2, "team": "radiant", "killer": 0, "aegisPlayer": 0 } ],
              "tormentors": [ { "t": 1250, "team": "dire", "killer": null } ]
            """);

        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(
            [
                new MapObjective(ObjectiveKind.Tower, 680, Team.Dire, 28, Team.Radiant, towerTier: 1, lane: Lane.Bottom),
                new MapObjective(ObjectiveKind.Tower, 1010, Team.Radiant, null, Team.Dire, towerTier: 2, lane: Lane.Top),
                new MapObjective(ObjectiveKind.Tower, 1100, Team.Dire, 28, Team.Dire, towerTier: 3, lane: Lane.Middle, denied: true),
                new MapObjective(ObjectiveKind.Tormentor, 1250, Team.Dire, null),
                new MapObjective(ObjectiveKind.Roshan, 1300, Team.Radiant, 31, aegisHeroId: 31),
                new MapObjective(ObjectiveKind.Barracks, 1668, Team.Dire, 28, Team.Radiant, lane: Lane.Bottom, meleeBarracks: false),
                new MapObjective(ObjectiveKind.Barracks, 1695, Team.Dire, 28, Team.Radiant, lane: Lane.Middle, meleeBarracks: true),
                new MapObjective(ObjectiveKind.Tower, 1888, Team.Dire, 28, Team.Radiant, towerTier: 4),
                new MapObjective(ObjectiveKind.Ancient, 1906, Team.Dire, 28, Team.Radiant),
            ],
            analysis.Objectives);
    }

    [Fact]
    public async Task Parse_WithRolesAndPositions_ComputesTheMetrics()
    {
        var body = MatchJson()
            .Replace("\"name\": \"me\" }", "\"name\": \"me\", \"role\": 1 }")
            .Replace("{ \"player\": 5, \"heroId\": 28, \"team\": \"dire\" }", "{ \"player\": 5, \"heroId\": 28, \"team\": \"dire\", \"role\": 1 }")
            .Replace("\"positions\": []", """
                "positions": [
                  { "player": 0, "samples": [ [0, -4000, -4000], [2, 5000, 5000.4] ] },
                  { "player": 5, "samples": [ [0, 4000, 4000] ] }
                ]
                """);

        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        var me = Assert.Single(analysis.Metrics!, hero => hero.HeroId == 31);
        Assert.Equal((1, 0.5, 2900), (me.Role, me.EnemySideShare, me.NetWorthAt10));
        // The positions also become the heatmaps (-4000,-4000 → cell 9,22; 5000,5000 → cell 24,7).
        var heatmap = Assert.Single(analysis.Heatmaps!, hero => hero.HeroId == 31);
        Assert.Equal([new HeatCell(24, 7, 1), new HeatCell(9, 22, 1)], heatmap.Phases.EarlyGame);
        Assert.Equal(2, analysis.Heatmaps!.Count);
    }

    [Fact]
    public async Task Parse_WithoutRoleQueue_InfersTheRoles()
    {
        // The default document has no roles (a pro match): the bottom-lane Radiant hero is the 1, the Dire jungler
        // takes the first role left.
        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, MatchJson(), "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.All(analysis.Metrics!, hero => Assert.True(hero.RoleInferred));
        Assert.Equal((1, 1), (analysis.Metrics!.Single(hero => hero.HeroId == 31).Role, analysis.Metrics!.Single(hero => hero.HeroId == 28).Role));
        Assert.False(analysis.IsOutdated);
    }

    [Fact]
    public async Task Parse_CombatPerMinute_GivesTheLaneDamage()
    {
        // Minutes 0..11 (the 11:30 match); the lane damage adds minutes 0..9 only.
        var body = MatchJson().Replace("{ \"player\": 0, \"damageToHeroes\": [0,0,0,0,0,0,0,0,0,0,0,0], \"damageFromHeroes\": [0,0,0,0,0,0,0,0,0,0,0,0], \"damageTaken\": [], \"healing\": [] }", """
            { "player": 0, "damageToHeroes": [0,100,100,100,100,100,100,100,100,100,999,999],
              "damageFromHeroes": [0,50,50,50,50,50,50,50,50,50,999,999], "damageTaken": [], "healing": [] }
            """);

        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        var me = Assert.Single(analysis.Metrics!, hero => hero.HeroId == 31);
        Assert.Equal((900, 450), (me.LaneDamageDealt, me.LaneDamageTaken));
        Assert.Equal(0, analysis.Metrics!.Single(hero => hero.HeroId == 28).LaneDamageDealt);
    }

    [Fact]
    public async Task Parse_WithoutCombat_IsAnUnreadableReplay()
    {
        // The whole combat object (one line in MatchJson) becomes null.
        var body = System.Text.RegularExpressions.Regex.Replace(MatchJson(), "\"combat\": \\{[^\\n]*\\},", "\"combat\": null,");

        await Assert.ThrowsAsync<ReplayUnreadableException>(() =>
            CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json"))).ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_KillOfAnUnknownPlayer_IsAnUnreadableReplay()
    {
        var body = MatchJson().Replace("\"kills\": []", """ "kills": [ { "t": 1, "victim": 7, "x": 0, "y": 0 } ] """);

        await Assert.ThrowsAsync<ReplayUnreadableException>(() =>
            CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json"))).ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_MatchEndedBeforeTenMinutes_UsesTheLastPoint()
    {
        // A 6:40 match: minutes 0..6 and the end — 8 points. The laning numbers are the end's, not minute 6's.
        var laning = """{ "endsAtSeconds": 400, "lanes": [ { "player": 0, "lane": "bottom" }, { "player": 5, "lane": "top" } ] }""";
        var short8Points = MatchJson(laning)
            .Replace("[600,700,800,900,1000,1200,1400,1600,1800,2000,2900,3000]", "[600,700,800,900,1000,1200,1400,1450]")
            .Replace("[0,100,200,300,400,500,600,700,800,900,3100,3300]", "[0,100,200,300,400,500,600,650]")
            .Replace("[0,1,2,3,4,5,6,7,8,9,12,13]", "[0,1,2,3,4,5,6,7]")
            .Replace("[0,0,0,0,1,1,1,2,2,2,3,3]", "[0,0,0,0,1,1,1,2]")
            .Replace("[0,0,0,0,0,0,1,1,1,1,1,1]", "[0,0,0,0,0,0,1,1]")
            .Replace("[600,800,1000,1200,1400,1600,1800,2000,2200,2400,3500,3600]", "[600,800,1000,1200,1400,1600,1800,1900]")
            .Replace("[0,150,300,450,600,750,900,1050,1200,1350,2800,2900]", "[0,150,300,450,600,750,900,950]")
            .Replace("[0,3,6,9,12,15,18,21,24,27,40,41]", "[0,3,6,9,12,15,18,20]")
            .Replace("[0,0,0,0,0,0,0,0,0,0,0,0]", "[0,0,0,0,0,0,0,0]")
            .Replace("690.4", "400.2");

        var analysis = await CreateClient(new FakeParser(Json(HttpStatusCode.OK, short8Points, "application/json")))
            .ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(400, analysis.EndsAtSeconds);
        Assert.Equal(new LaningStats(31, Team.Radiant, Lane.Bottom, 7, 2, 1450, 650, 1), analysis.FindHero(31));
        Assert.Equal([0, 60, 120, 180, 240, 300, 360, 400], analysis.Timeline!.Seconds);
    }

    [Fact]
    public async Task Parse_WithoutApiKey_SendsNoAuthorization()
    {
        // The parser accepts that only when run locally without a key (go run).
        var parser = new FakeParser(Json(HttpStatusCode.OK, MatchJson(), "application/json"));

        await CreateClient(parser, apiKey: "").ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Null(Assert.Single(parser.Requests).Authorization);
    }

    [Fact]
    public async Task Parse_Problem404_IsAnExpiredReplay()
    {
        var client = CreateClient(new FakeParser(Problem(HttpStatusCode.NotFound)));

        await Assert.ThrowsAsync<ReplayExpiredException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)] // the replay can't be read
    [InlineData(HttpStatusCode.BadRequest)] // the parser refused our request (our bug)
    [InlineData(HttpStatusCode.BadGateway)] // could not download from Valve this time: the parser itself is fine
    public async Task Parse_Problem422Or400Or502_IsAnUnreadableReplay(HttpStatusCode status)
    {
        var parser = new FakeParser(Problem(status));

        await Assert.ThrowsAsync<ReplayUnreadableException>(
            () => CreateClient(parser).ParseAsync(Match, ReplayUrl, CancellationToken.None));
        Assert.Single(parser.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)] // wrong key: configuration, not the replay
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Parse_OtherProblems_MeanTheParserIsUnavailable(HttpStatusCode status)
    {
        var client = CreateClient(new FakeParser(Problem(status)));

        var exception = await Assert.ThrowsAsync<ReplayParserUnavailableException>(
            () => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
        Assert.DoesNotContain(ApiKey, exception.ToString());
    }

    // ----- Busy (problem+json 503 with Retry-After) -----

    [Fact]
    public async Task Parse_Busy_WaitsRetryAfterAndTriesAgain()
    {
        var parser = new FakeParser(Busy(TimeSpan.FromSeconds(7)), Json(HttpStatusCode.OK, MatchJson(), "application/json"));
        var clock = new InstantClock();

        var analysis = await CreateClient(parser, clock: clock).ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal(600, analysis.EndsAtSeconds);
        Assert.Equal([TimeSpan.FromSeconds(7)], clock.Waits);
        // A new request each time (an HttpRequestMessage can't be sent twice), with the same content.
        Assert.Equal(2, parser.Requests.Count);
        Assert.All(parser.Requests, request => Assert.Equal($"Bearer {ApiKey}", request.Authorization));
        Assert.Equal(parser.Requests[0].Body, parser.Requests[1].Body);
    }

    [Fact]
    public async Task Parse_StillBusyAfterAllAttempts_IsBusy_NotUnavailable()
    {
        var parser = new FakeParser(
            Busy(TimeSpan.FromMinutes(2)), // capped
            Busy(retryAfter: null), // no Retry-After: the default wait
            Busy(TimeSpan.FromSeconds(1)));
        var clock = new InstantClock();

        await Assert.ThrowsAsync<ReplayParserBusyException>(
            () => CreateClient(parser, clock: clock).ParseAsync(Match, ReplayUrl, CancellationToken.None));

        Assert.Equal(ReplayParserClient.MaxAttempts, parser.Requests.Count);
        // No wait after the last attempt.
        Assert.Equal([ReplayParserClient.MaxBusyWait, ReplayParserClient.DefaultBusyWait], clock.Waits);
        Assert.Equal(TimeSpan.FromSeconds(30), ReplayParserClient.MaxBusyWait);
    }

    [Fact]
    public async Task Parse_BusyWithRetryAfterDate_WaitsUntilThatDate()
    {
        var clock = new InstantClock();
        var busy = Problem(HttpStatusCode.ServiceUnavailable);
        busy.Headers.RetryAfter = new RetryConditionHeaderValue(clock.Now.AddSeconds(12));
        var parser = new FakeParser(busy, Json(HttpStatusCode.OK, MatchJson(), "application/json"));

        await CreateClient(parser, clock: clock).ParseAsync(Match, ReplayUrl, CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(12)], clock.Waits);
    }

    [Fact]
    public async Task Parse_503NotFromTheParser_IsUnavailable_WithoutRetrying()
    {
        // ngrok (or a proxy) saying 503 says nothing about our parser being busy.
        var response = Json(HttpStatusCode.ServiceUnavailable, "<html>Service unavailable</html>", "text/html");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        var parser = new FakeParser(response);

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(
            () => CreateClient(parser).ParseAsync(Match, ReplayUrl, CancellationToken.None));
        Assert.Single(parser.Requests);
    }

    [Fact]
    public async Task Parse_CancelledWhileWaitingForABusyParser_StopsWaiting()
    {
        var parser = new FakeParser(Busy(TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();
        var clock = new InstantClock { Fire = false, OnWait = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(parser, clock: clock).ParseAsync(Match, ReplayUrl, cancellation.Token));
        Assert.Single(parser.Requests);
    }

    [Fact]
    public async Task Parse_NgrokPageWith404_IsUnavailable_NotExpired()
    {
        // What ngrok answers by itself when the tunnel is offline (the machine running the parser is off).
        var response = Json(HttpStatusCode.NotFound, "<!DOCTYPE html><html><body>ERR_NGROK_3200</body></html>", "text/html");
        response.Headers.Add("ngrok-error-code", "ERR_NGROK_3200");
        var client = CreateClient(new FakeParser(response));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "text/plain", "Tunnel not found")]
    [InlineData(HttpStatusCode.NotFound, "application/json", """{"error": "not found"}""")] // JSON, but not ProblemDetails
    [InlineData(HttpStatusCode.BadGateway, "text/html", "<html>Bad gateway</html>")]
    [InlineData(HttpStatusCode.OK, "text/html", "<html>You are about to visit...</html>")] // ngrok's warning page
    [InlineData(HttpStatusCode.Found, "text/html", "")] // a redirect
    public async Task Parse_AnswersNotFromTheParser_AreUnavailable(HttpStatusCode status, string contentType, string body)
    {
        var client = CreateClient(new FakeParser(Json(status, body, contentType)));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_ProblemWithNgrokErrorHeader_IsUnavailable()
    {
        // Anything carrying ngrok's error header was answered by ngrok, whatever it looks like.
        var response = Problem(HttpStatusCode.NotFound);
        response.Headers.Add("ngrok-error-code", "ERR_NGROK_8012");
        var client = CreateClient(new FakeParser(response));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_ConnectionRefused_IsUnavailable()
    {
        var client = CreateClient(new FakeParser(_ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused"))));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_Timeout_IsUnavailable()
    {
        var parser = new FakeParser(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Json(HttpStatusCode.OK, MatchJson(), "application/json");
        });
        var client = CreateClient(parser, timeout: TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<ReplayParserUnavailableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
    }

    [Fact]
    public async Task Parse_CancelledByTheCaller_IsNotTranslated()
    {
        // The app stopping is not "the parser is unavailable".
        var parser = new FakeParser(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Json(HttpStatusCode.OK, MatchJson(), "application/json");
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(parser).ParseAsync(Match, ReplayUrl, cancellation.Token));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"players": []}""")] // nothing else
    public async Task Parse_MalformedAnswer_IsAnUnreadableReplay(string body)
    {
        var archive = new FakeArchive();
        var client = CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")), archive: archive);

        await Assert.ThrowsAsync<ReplayUnreadableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
        Assert.Empty(archive.Kept);
    }

    // Each case breaks one thing in an otherwise valid document.
    public static TheoryData<string, string> InvalidDocuments() => new()
    {
        { "schemaVersion 0", MatchJson(schemaVersion: 0) },
        { "no duration", MatchJson().Replace("\"durationSeconds\": 690.4", "\"durationSecondz\": 690.4") },
        { "no laning", MatchJson().Replace("\"laning\":", "\"laningz\":") },
        { "laning past 10:00", MatchJson("""{ "endsAtSeconds": 700, "lanes": [ { "player": 0, "lane": "bottom" }, { "player": 5, "lane": "jungle" } ] }""") },
        { "unknown lane", MatchJson().Replace("\"jungle\"", "\"sideways\"") },
        { "lane missing for a player", MatchJson("""{ "endsAtSeconds": 600, "lanes": [ { "player": 0, "lane": "bottom" } ] }""") },
        { "unknown team", MatchJson().Replace("\"dire\"", "\"neutral\"") },
        { "negative net worth", MatchJson().Replace("[600,700,", "[-600,700,") },
        { "points differ between players", MatchJson().Replace("2400,3500,3600]", "2400,3500]") },
        { "no xp", MatchJson().Replace("\"xp\": [0,100,", "\"xq\": [0,100,") },
        { "same hero twice", MatchJson().Replace("\"heroId\": 28", "\"heroId\": 31") },
        { "same player index twice", MatchJson().Replace("{ \"player\": 5, \"netWorth\"", "{ \"player\": 0, \"netWorth\"") },
    };

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public async Task Parse_InvalidDocument_IsAnUnreadableReplay_AndIsNotKept(string _, string body)
    {
        var archive = new FakeArchive();
        var client = CreateClient(new FakeParser(Json(HttpStatusCode.OK, body, "application/json")), archive: archive);

        await Assert.ThrowsAsync<ReplayUnreadableException>(() => client.ParseAsync(Match, ReplayUrl, CancellationToken.None));
        Assert.Empty(archive.Kept);
    }

    private static ReplayParserClient CreateClient(
        FakeParser parser, string apiKey = ApiKey, TimeSpan? timeout = null, TimeProvider? clock = null,
        FakeArchive? archive = null) =>
        new(
            new HttpClient(parser) { BaseAddress = new Uri("https://parser.example/"), Timeout = timeout ?? TimeSpan.FromSeconds(10) },
            Options.Create(new ReplayParserOptions { BaseUrl = new Uri("https://parser.example/"), ApiKey = apiKey }),
            // By default a "busy" answer would really wait: tests that expect one pass an InstantClock.
            clock ?? TimeProvider.System,
            archive ?? new FakeArchive(),
            NullLogger<ReplayParserClient>.Instance);

    private sealed class FakeArchive : IReplayDocumentArchive
    {
        public List<(long MatchId, int SchemaVersion, string Document)> Kept { get; } = [];

        public Task KeepAsync(MatchId matchId, int schemaVersion, string document, CancellationToken cancellationToken)
        {
            Kept.Add((matchId.Value, schemaVersion, document));
            return Task.CompletedTask;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, string contentType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    private static HttpResponseMessage Problem(HttpStatusCode status) =>
        Json(status, $$"""{"title": "error", "status": {{(int)status}}, "detail": "details"}""", "application/problem+json");

    private static HttpResponseMessage Busy(TimeSpan? retryAfter)
    {
        var response = Problem(HttpStatusCode.ServiceUnavailable);
        if (retryAfter is { } delta)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        }

        return response;
    }

    // A clock whose waits (Task.Delay through it) end at once and are remembered. Fire = false: they never end.
    private sealed class InstantClock : TimeProvider
    {
        public DateTimeOffset Now { get; } = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        public bool Fire { get; init; } = true;

        // Called when a wait starts.
        public Action? OnWait { get; init; }

        public List<TimeSpan> Waits { get; } = [];

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (Waits)
            {
                Waits.Add(dueTime);
            }

            var timer = base.CreateTimer(callback, state, Fire ? TimeSpan.Zero : Timeout.InfiniteTimeSpan, period);
            OnWait?.Invoke();
            return timer;
        }
    }

    internal sealed record SentRequest(HttpMethod Method, string Uri, string? Authorization, string? NgrokSkipBrowserWarning, string Body);

    // Answers every request with the given function, and remembers what was sent.
    private sealed class FakeParser(Func<CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        // Answers the requests with these responses, in order (one each).
        public FakeParser(params HttpResponseMessage[] responses) : this(Answers(responses))
        {
        }

        private static Func<CancellationToken, Task<HttpResponseMessage>> Answers(HttpResponseMessage[] responses)
        {
            var queue = new Queue<HttpResponseMessage>(responses);
            return _ => Task.FromResult(queue.Dequeue());
        }

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("ngrok-skip-browser-warning", out var values) ? values.Single() : null,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await answer(cancellationToken);
        }
    }
}

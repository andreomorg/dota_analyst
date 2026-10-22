using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

// GET /api/internal/replay-facts/{matchId} (work/data-flow-audit D18): machine key, validation, windows, final facts.
public class ReplayFactsApiTests
{
    private const string Key = "test-machine-key-for-facts";
    private static Dictionary<string, string?> Settings => new()
    {
        ["CoachMl:Enabled"] = "true", ["CoachMl:BaseUrl"] = "http://coach.invalid/", ["CoachMl:ApiKey"] = Key,
    };

    [Fact]
    public async Task Disabled_IsNotFound_WithoutKey_IsUnauthorized()
    {
        using (var disabled = new ApiFactory())
        {
            using var client = disabled.CreateClient();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/internal/replay-facts/123?heroId=1&seconds=10")).StatusCode);
        }

        using var factory = Factory(new Archive(Replay()));
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/internal/replay-facts/123?heroId=1&seconds=10")).StatusCode);
    }

    [Theory]
    [InlineData("heroId=1&seconds=10&windowSeconds=301")]      // window too long
    [InlineData("heroId=0&seconds=10")]
    [InlineData("heroId=2&seconds=10")]                         // not in the match
    [InlineData("heroId=1&seconds=61")]                         // after the end (60.4)
    [InlineData("heroId=1&seconds=60&includeFinal=true")]       // a final query is at ceil(60.4) = 61
    public async Task InvalidQueries_AreBadRequests(string query)
    {
        using var factory = Factory(new Archive(Replay()));
        using var client = Client(factory);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/internal/replay-facts/123?" + query)).StatusCode);
    }

    [Fact]
    public async Task NoDocument_IsNotFound()
    {
        using var factory = Factory(new Archive(null));
        using var client = Client(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/internal/replay-facts/123?heroId=1&seconds=10")).StatusCode);
    }

    [Fact]
    public async Task Facts_WindowSnapshotAndNoFinalByDefault()
    {
        var archive = new Archive(Replay());
        using var factory = Factory(archive);
        using var client = Client(factory);

        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/internal/replay-facts/123?heroId=1&seconds=10&windowSeconds=8"));
        var root = json.RootElement;

        Assert.Equal((10, 2), (root.GetProperty("seconds").GetInt32(), root.GetProperty("windowStart").GetInt32()));
        Assert.Equal("found", root.GetProperty("snapshot").GetProperty("hero").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("final").ValueKind);
        Assert.Equal("available", root.GetProperty("availability").GetProperty("finalStats").GetString());
        Assert.Equal("unavailable", root.GetProperty("availability").GetProperty("chatWheel").GetString());
        Assert.Equal(1, archive.Reads);
        Assert.DoesNotContain("steam", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingDuration_DoesNotUseLaningEndAsMatchEnd()
    {
        using var factory = Factory(new Archive(Replay() with { Metadata = null }));
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/internal/replay-facts/123?heroId=1&seconds=60&includeFinal=true")).StatusCode);
    }

    [Fact]
    public async Task Snapshot_HidesFuturePossessionEnd_AndReadsTormentorAsOfAnchor()
    {
        var replay = Replay() with { Facts = new ReplayAdditionalFacts
        {
            StolenAbilities = [new(1, 5, 40, "test_spell", 100, 2, 3, false)],
            Tormentors = [new(Team.Radiant, 1, 2, [new(8, true), new(12, false)])],
        } };
        using var factory = Factory(new Archive(replay));
        using var client = Client(factory);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/internal/replay-facts/123?heroId=1&seconds=10"));
        var snapshot = json.RootElement.GetProperty("snapshot");
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("stolenHeld")[0].GetProperty("to").ValueKind);
        var observation = snapshot.GetProperty("tormentors")[0].GetProperty("observation");
        Assert.Equal(8, observation.GetProperty("seconds").GetInt32());
        Assert.True(observation.GetProperty("alive").GetBoolean());
        Assert.Equal("omniscientReplay", json.RootElement.GetProperty("observationScope").GetString());
    }

    [Fact]
    public async Task FinalQuery_PreservesFractionalEvents_ExcludesPostgame()
    {
        var replay = Replay() with { Facts = new ReplayAdditionalFacts
        {
            CourierDeaths = [new(60.2, 1, null), new(60.5, 1, null)],
            ChatWheel = [new(1, 60.2, 17), new(1, 60.5, 18)],
            StolenAbilities = [new(1, 60.5, null, "postgame_spell", 100, 1, 1, false)],
            Tormentors = [new(Team.Radiant, 1, 2, [new(60, true), new(61, false)])],
        } };
        using var factory = Factory(new Archive(replay));
        using var client = Client(factory);
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/internal/replay-facts/123?heroId=1&seconds=61&includeFinal=true"));
        Assert.Equal(60.4, json.RootElement.GetProperty("exactEventsEnd").GetDouble());
        var events = json.RootElement.GetProperty("events");
        Assert.Equal(1, events.GetProperty("courierDeaths").GetArrayLength());
        var phrases = events.GetProperty("communication").GetProperty("phrasesById");
        Assert.True(phrases.TryGetProperty("17", out _));
        Assert.False(phrases.TryGetProperty("18", out _));
        var snapshot = json.RootElement.GetProperty("snapshot");
        Assert.Equal(0, snapshot.GetProperty("stolenHeld").GetArrayLength());
        Assert.True(snapshot.GetProperty("tormentors")[0].GetProperty("observation").GetProperty("alive").GetBoolean());
    }

    [Fact]
    public async Task FinalQuery_AtTheEnd_HasTheWholeMatchFacts()
    {
        using var factory = Factory(new Archive(Replay()));
        using var client = Client(factory);

        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/internal/replay-facts/123?heroId=1&seconds=61&includeFinal=true"));
        var root = json.RootElement;

        Assert.Equal(60, root.GetProperty("eventsEnd").GetInt32());                         // the real end, not 61
        Assert.Equal(7, root.GetProperty("final").GetProperty("stats").GetProperty("kills").GetInt32());
    }

    private static ArchivedReplay Replay()
    {
        MatchId.TryCreate(123, out var id);
        var analysis = new ReplayAnalysis(id, 60, [new(1, Team.Radiant, Lane.Bottom, 0, 0, 0, 0, 0)],
            new MatchTimeline([0, 60], [new(1, Team.Radiant, [0, 0], [0, 0])]));
        var facts = new ReplayAdditionalFacts
        {
            FinalStats = [new FinalHeroStats(1, 3, 7, 0, 1, 20, 2, 5, 3000, 2800, 1500, 900, 0, 0, 0, 0.5, 0, 0, 0, 0, 0, 100)],
        };
        return new ArchivedReplay(analysis, [new HeroPositions(1, [new PositionSample(10, 100, 200)])],
            new ReplayActivity([], [], []), DateTimeOffset.UnixEpoch, new ReplayMetadata(60.4, 6962, null, true), facts);
    }

    // The app with the internal routes on and the archive replaced (the factory is disposed with the app it made).
    private static WebApplicationFactory<Program> Factory(Archive archive) => new ApiFactory(Settings)
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IArchivedReplaySource>(archive)));

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        return client;
    }

    private sealed class Archive(ArchivedReplay? replay) : IArchivedReplaySource
    {
        public int Reads { get; private set; }

        public Task<ArchivedReplay?> FindAsync(MatchId matchId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(replay);
        }
    }
}

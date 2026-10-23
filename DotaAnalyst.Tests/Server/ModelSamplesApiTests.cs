using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

public sealed class ModelSamplesApiTests : IDisposable
{
    private readonly List<IDisposable> factories = [];

    public void Dispose() => factories.ForEach(factory => factory.Dispose());

    private const string Key = "test-machine-key-for-models";
    private const string Route = "/api/internal/death-risk/samples/";

    private static Dictionary<string, string?> Settings => new()
    {
        ["CoachMl:Enabled"] = "true",
        ["CoachMl:BaseUrl"] = "http://coach.invalid/",
        ["CoachMl:ApiKey"] = Key,
    };

    [Fact]
    public async Task Disabled_IsNotFound()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route + "1")).StatusCode);
    }

    [Fact]
    public async Task WithoutTheMachineKey_IsUnauthorized_AndReadsNothing()
    {
        var source = new Source(Replay());
        using var client = Client(source);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key-of-same-size-xx");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Route + "1")).StatusCode);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task NoKeptReplay_IsNotFound_AndAnOutdatedOne_Unprocessable()
    {
        using var missing = Client(new Source(null), Key);
        using var outdated = Client(new Source(new ArchivedReplay(new ReplayAnalysis(Match(), 60, Players), [])), Key);

        Assert.Equal(HttpStatusCode.NotFound, (await missing.GetAsync(Route + "1")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await outdated.GetAsync(Route + "1")).StatusCode);
    }

    [Fact]
    public async Task Samples_ComeFlat_WithTheVersionAndTheLabel()
    {
        using var client = Client(new Source(Replay()), Key);

        using var response = await client.GetAsync(Route + "1");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var first = root.GetProperty("samples")[0].GetProperty("situation");

        Assert.Equal((2, "1", 10, 15), (root.GetProperty("featureVersion").GetInt32(), root.GetProperty("matchId").GetString(),
            root.GetProperty("sampleEverySeconds").GetInt32(), root.GetProperty("horizonSeconds").GetInt32()));
        // 0, 10 ... 40 for the one hero with positions (the match ends at 1:00); it dies at 0:25.
        Assert.Equal(5, root.GetProperty("samples").GetArrayLength());
        Assert.Equal("radiant", first.GetProperty("team").GetString());
        Assert.True(first.TryGetProperty("hiddenEnemiesNear", out _));
        // The fake replay has no activity: unknown, not "nothing used".
        Assert.False(first.GetProperty("activityKnown").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("teleportSecondsAgo").ValueKind);
        Assert.True(root.GetProperty("samples")[2].GetProperty("diesSoon").GetBoolean());
        Assert.Equal(25, root.GetProperty("samples")[2].GetProperty("diedAtSeconds").GetInt32());
    }

    [Fact]
    public async Task PositionEstimates_GiveEachHerosEstimates_AndNeedTheMachineKey()
    {
        using var client = Client(new Source(Replay()), Key);
        using var anonymous = Client(new Source(Replay()));

        using var response = await client.GetAsync("/api/internal/position-estimates/1");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var heroes = json.RootElement.GetProperty("heroes");

        Assert.Equal("1", json.RootElement.GetProperty("matchId").GetString());
        Assert.True(heroes.GetArrayLength() > 0);
        // The match ends at 1:00: only the 1:00 checkpoint.
        Assert.All(heroes.EnumerateArray(), hero => Assert.Equal(60, hero.GetProperty("estimates")[0].GetProperty("second").GetInt32()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/internal/position-estimates/1")).StatusCode);
    }

    [Fact]
    public async Task NextRegionSamples_SayWhereTheHeroWasLater()
    {
        using var client = Client(new Source(Replay()), Key);

        using var response = await client.GetAsync("/api/internal/next-region/samples/1");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var samples = json.RootElement.GetProperty("samples");

        Assert.Equal(30, json.RootElement.GetProperty("horizonSeconds").GetInt32());
        // 0, 10, 20 and 30 (30 + 30 = 1:00, the end); the hero stands at the center: the river or a lane, never null.
        Assert.Equal(4, samples.GetArrayLength());
        Assert.Equal(JsonValueKind.String, samples[0].GetProperty("regionLater").ValueKind);
        Assert.Equal(0, samples[0].GetProperty("situation").GetProperty("seconds").GetInt32());
    }

    [Fact]
    public async Task Refresh_NeedsTheSwitchAndTheMachineKey()
    {
        using var off = new ApiFactory();
        using var offClient = off.CreateClient();
        using var noKey = Client(new Source(Replay()));

        Assert.Equal(HttpStatusCode.NotFound, (await offClient.PostAsync("/api/internal/replays/1/refresh", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await noKey.PostAsync("/api/internal/replays/1/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task InvalidMatchId_IsBadRequest()
    {
        using var client = Client(new Source(Replay()), Key);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Route + "0")).StatusCode);
    }

    private static readonly LaningStats[] Players =
    [
        new(1, Team.Radiant, Lane.Bottom, 0, 0, 600, 0, 0),
        new(6, Team.Dire, Lane.Top, 0, 0, 600, 0, 0),
    ];

    private static ArchivedReplay Replay()
    {
        var timeline = new MatchTimeline(
            [0, 60],
            [new HeroTimeline(1, Team.Radiant, [600, 700], [0, 100]), new HeroTimeline(6, Team.Dire, [600, 700], [0, 100])]);
        var analysis = new ReplayAnalysis(Match(), 60, Players, timeline,
            deaths: [new HeroDeath(1, 25, 0, 0, KillerKind.Hero, 6, reincarnated: true)], wards: [], objectives: []);
        var positions = new HeroPositions(1, Enumerable.Range(0, 31).Select(i => new PositionSample(i * 2, 0, 0)).ToList());
        return new ArchivedReplay(analysis, [positions]);
    }

    private static MatchId Match() => MatchId.TryCreate(1, out var id) ? id : throw new InvalidOperationException();

    private HttpClient Client(Source source, string? key = null)
    {
        var factory = new ApiFactory(Settings);
        var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IArchivedReplaySource>(source)));
        factories.Add(app);
        factories.Add(factory);
        var client = app.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        return client;
    }

    private sealed class Source(ArchivedReplay? replay) : IArchivedReplaySource
    {
        public int Calls { get; private set; }

        public Task<ArchivedReplay?> FindAsync(MatchId matchId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(replay);
        }
    }
}

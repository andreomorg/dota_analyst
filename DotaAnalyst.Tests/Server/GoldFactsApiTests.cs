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

public class GoldFactsApiTests
{
    private const string Key = "gold-facts-test-machine-key";
    private const string Query = "/api/internal/gold-facts/123?heroId=1&seconds=60.4";

    [Fact]
    public async Task DisabledOrUnauthorized_DoesNotReadArchive()
    {
        using var disabled = new ApiFactory();
        using var disabledClient = disabled.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await disabledClient.GetAsync(Query)).StatusCode);
        var archive = new Archive(Replay());
        using var factory = Factory(archive);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Query)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Query)).StatusCode);
        Assert.Equal(0, archive.Reads);
    }

    [Fact]
    public async Task Authorized_ReturnsFractionalWindowAndObservedChanges()
    {
        var archive = new Archive(Replay());
        using var factory = Factory(archive);
        using var client = Client(factory);
        using var response = await client.GetAsync(Query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("123", root.GetProperty("matchId").GetString());
        var window = root.GetProperty("window");
        Assert.Equal(60.4, window.GetProperty("to").GetDouble());
        Assert.Equal(40, window.GetProperty("changes").GetProperty("netChange").GetInt64());
        Assert.Equal(1, archive.Reads);
    }

    [Theory]
    [InlineData("/api/internal/gold-facts/0?heroId=1&seconds=10")]
    [InlineData("/api/internal/gold-facts/123?heroId=0&seconds=10")]
    [InlineData("/api/internal/gold-facts/123?heroId=2&seconds=10")]
    [InlineData("/api/internal/gold-facts/123?heroId=1&seconds=61")]
    [InlineData("/api/internal/gold-facts/123?heroId=1&seconds=10&windowSeconds=301")]
    public async Task InvalidQuery_IsBadRequest(string query)
    {
        using var factory = Factory(new Archive(Replay()));
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(query)).StatusCode);
    }

    [Theory]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("malformed", HttpStatusCode.UnprocessableEntity)]
    [InlineData("storage", HttpStatusCode.ServiceUnavailable)]
    [InlineData("overflow", HttpStatusCode.UnprocessableEntity)]
    public async Task ArchiveFailures_ReturnProblemDetails(string failure, HttpStatusCode expected)
    {
        var replay = failure == "overflow" ? Replay() with
        { Facts = new ReplayAdditionalFacts { GoldEvents = [new(1, 60, long.MinValue, 1, null, null)] } } : null;
        using var factory = Factory(new Archive(replay, failure));
        using var client = Client(factory);
        using var response = await client.GetAsync(Query);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static ArchivedReplay Replay()
    {
        MatchId.TryCreate(123, out var id);
        return new(new ReplayAnalysis(id, 60, [new(1, Team.Radiant, Lane.Bottom, 0, 0, 0, 0, 0)]), [],
            Metadata: new ReplayMetadata(60.4, 6962, null, true),
            Facts: new ReplayAdditionalFacts { GoldEvents = [new(1, 60.4, 40, 13, null, null), new(1, 60.41, 999, 13, null, null)] });
    }

    private static WebApplicationFactory<Program> Factory(Archive archive) => new ApiFactory(new Dictionary<string, string?>
    {
        ["CoachMl:Enabled"] = "true",
        ["CoachMl:BaseUrl"] = "http://coach.invalid/",
        ["CoachMl:ApiKey"] = Key,
    }).WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IArchivedReplaySource>(archive)));

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Key);
        return client;
    }

    private sealed class Archive(ArchivedReplay? replay, string? failure = null) : IArchivedReplaySource
    {
        public int Reads { get; private set; }
        public Task<ArchivedReplay?> FindAsync(MatchId matchId, CancellationToken cancellationToken)
        {
            Reads++;
            if (failure == "malformed") throw new InvalidDataException("test");
            if (failure == "storage") throw new StorageUnavailableException("test");
            return Task.FromResult(replay);
        }
    }
}

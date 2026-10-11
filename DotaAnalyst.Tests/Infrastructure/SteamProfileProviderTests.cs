using System.Net;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Infrastructure.Steam;
using DotaAnalyst.Tests.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Tests.Infrastructure;

public class SteamProfileProviderTests
{
    private static readonly AccountId Me = MatchPlayerBuilder.Account(99517715);

    // Shape of GetPlayerSummaries/v2 as documented by Valve (trimmed to a few fields).
    private const string SummariesJson = """
        {
          "response": {
            "players": [
              {
                "steamid": "76561198059783443",
                "communityvisibilitystate": 3,
                "personaname": "Andreo",
                "avatar": "https://avatars.steamstatic.com/abc.jpg",
                "avatarfull": "https://avatars.steamstatic.com/abc_full.jpg"
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task GetProfile_ReadsNameAndAvatar_AndCachesIt()
    {
        var steam = new StubSteam(HttpStatusCode.OK, SummariesJson);
        var provider = CreateProvider(steam);

        var first = await provider.GetProfileAsync(Me, CancellationToken.None);
        var second = await provider.GetProfileAsync(Me, CancellationToken.None);

        Assert.Equal(new PlayerProfile("Andreo", new Uri("https://avatars.steamstatic.com/abc_full.jpg")), first);
        Assert.Same(first, second);
        Assert.Equal(1, steam.Calls);
        Assert.Contains("steamids=76561198059783443", steam.LastQuery);
    }

    [Fact]
    public async Task GetProfile_UnknownAccount_ReturnsNull()
    {
        var provider = CreateProvider(new StubSteam(HttpStatusCode.OK, """{"response":{"players":[]}}"""));

        Assert.Null(await provider.GetProfileAsync(Me, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "")] // what Steam answers to a wrong API key
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task GetProfile_SteamFails_ThrowsDataSourceUnavailable_WithoutTheKey(HttpStatusCode status, string body)
    {
        var provider = CreateProvider(new StubSteam(status, body));

        var exception = await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => provider.GetProfileAsync(Me, CancellationToken.None));

        Assert.DoesNotContain("secret-key", exception.ToString());
    }

    [Theory]
    [InlineData("http://avatars.steamstatic.com/a.jpg")] // not https
    [InlineData("javascript:alert(1)")]
    [InlineData(null)]
    public void ToProfile_OnlyKeepsHttpsAvatars(string? avatar)
    {
        var profile = SteamProfileProvider.ToProfile(new SteamPlayerSummary("76561198059783443", "Andreo", avatar));

        Assert.NotNull(profile);
        Assert.Null(profile.AvatarUrl);
    }

    private static SteamProfileProvider CreateProvider(StubSteam steam) =>
        new(
            new HttpClient(steam) { BaseAddress = new Uri("https://api.steampowered.com/") },
            Options.Create(new SteamOptions { ApiKey = "secret-key" }),
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000 }));

    private sealed class StubSteam(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string LastQuery { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = request.RequestUri?.Query ?? "";
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(body) });
        }
    }
}

using System.Net;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.Heroes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

public sealed class HeroProfileApiTests
{
    [Fact]
    public async Task KnownHistoricalRosterDoesNotIntroduceHeroesFromCurrentFallback()
    {
        using var factory = new ApiFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IHeroCatalog>(new Heroes());
            services.AddSingleton<IGameBuildCatalog>(new Historical());
        }));
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/heroes/31/profile?at=2026-04-01T00:00:00Z")).StatusCode);
    }

    private sealed class Historical : IGameBuildCatalog
    {
        public Task<(GameBuild Build, GameCatalog Catalog)?> ForMatchAsync(DateTimeOffset? at, CancellationToken ct) =>
            Task.FromResult<(GameBuild, GameCatalog)?>((new("historical", 1, DateTimeOffset.UnixEpoch),
                new(Heroes: new Dictionary<int, HeroDetails>())));
    }

    [Theory]
    [InlineData("0", HttpStatusCode.BadRequest)]
    [InlineData("bad", HttpStatusCode.BadRequest)]
    [InlineData("99999", HttpStatusCode.NotFound)]
    public async Task InvalidOrUnknownProfileHasProblemResponse(string id, HttpStatusCode status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/api/v1/heroes/{id}/profile");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CurrentFallbackIsExplicitAndDoesNotInventBaseStats()
    {
        using var factory = new ApiFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IHeroCatalog>(new Heroes())));
        using var client = app.CreateClient();
        var response = await client.GetAsync("/api/v1/heroes/31/profile?at=2000-01-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("currentFallback", json.RootElement.GetProperty("catalogScope").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("build").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("details").GetProperty("profile").ValueKind);
    }

    private sealed class Heroes : IHeroCatalog
    {
        public Task<IReadOnlyDictionary<int, Hero>> GetHeroesAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<int, Hero>>(new Dictionary<int, Hero>
            { [31] = new(31, "Lich") { Details = new(HeroAttribute.Intelligence, HeroAttackType.Ranged, new HashSet<HeroRole> { HeroRole.Support }) } });
    }
}

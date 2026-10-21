using System.Net;
using System.Text.Json;

namespace DotaAnalyst.Tests.Server;

public sealed class KitApiTests
{
    [Fact]
    public async Task Kit_ExposesFullAbilityTalentsFacetsAndPatch()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/heroes/31/kit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var kit = json.RootElement;
        Assert.Equal("7.41", kit.GetProperty("patch").GetString());
        var ability = Assert.Single(kit.GetProperty("abilities").EnumerateArray());
        Assert.Equal("enemy", ability.GetProperty("targetTeam").GetString());
        Assert.Equal("magical", ability.GetProperty("damageType").GetString());
        Assert.False(ability.GetProperty("piercesSpellImmunity").GetBoolean());
        Assert.Equal(4, ability.GetProperty("manaCost").GetArrayLength());
        Assert.Equal(200, ability.GetProperty("values")[0].GetProperty("levels")[3].GetDouble());
        Assert.Equal(1, kit.GetProperty("talents")[0].GetProperty("tier").GetInt32());
        Assert.True(kit.GetProperty("facets")[0].GetProperty("deprecated").GetBoolean());
    }

    [Theory]
    [InlineData("/api/v1/heroes/99999/kit")]
    [InlineData("/api/v1/items/missing")]
    public async Task Unknown_ReturnsProblem(string path)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnavailableKit_IsEmptyForKnownHero()
    {
        using var factory = new ApiFactory();
        factory.Abilities.Unavailable = true;
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/heroes/31/kit"));
        Assert.Equal(0, json.RootElement.GetProperty("abilities").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("patch").ValueKind);
    }

    [Fact]
    public async Task Item_ExposesFullDetails()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/items/blink"));
        var item = json.RootElement;
        Assert.Equal("blink", item.GetProperty("key").GetString());
        Assert.Equal(2250, item.GetProperty("cost").GetInt32());
        Assert.Equal(15, item.GetProperty("cooldown").GetDouble());
        Assert.Equal("active", item.GetProperty("abilities")[0].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("manaCost").ValueKind);
    }
}

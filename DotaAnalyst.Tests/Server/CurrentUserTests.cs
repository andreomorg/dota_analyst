using DotaAnalyst.Server.Authentication;

namespace DotaAnalyst.Tests.Server;

public class CurrentUserTests
{
    [Fact]
    public void TryParseSteamIdentifier_SteamId64_GivesTheAccountId()
    {
        Assert.True(CurrentUser.TryParseSteamIdentifier("https://steamcommunity.com/openid/id/76561198059783443", out var accountId));
        Assert.Equal(99517715, accountId.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://steamcommunity.com/openid/id/99517715")] // an account ID is not what Steam sends
    [InlineData("http://steamcommunity.com/openid/id/76561198059783443")] // not https
    [InlineData("https://steamcommunity.com.evil.example/openid/id/76561198059783443")]
    [InlineData("https://steamcommunity.com/openid/id/76561197960265728")] // the offset itself = account 0
    [InlineData("https://steamcommunity.com/openid/id/76561198059783443/extra")]
    public void TryParseSteamIdentifier_AnythingElse_IsRefused(string? identifier)
    {
        Assert.False(CurrentUser.TryParseSteamIdentifier(identifier, out _));
    }

    [Fact]
    public void Principal_RoundTripsTheAccountId()
    {
        Assert.True(CurrentUser.TryParseSteamIdentifier("https://steamcommunity.com/openid/id/76561198059783443", out var accountId));

        var principal = CurrentUser.CreatePrincipal(accountId, "Test");

        Assert.True(CurrentUser.TryGetAccountId(principal, out var readBack));
        Assert.Equal(accountId, readBack);
    }
}

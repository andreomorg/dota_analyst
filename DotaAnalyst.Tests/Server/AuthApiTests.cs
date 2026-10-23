using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DotaAnalyst.Domain.Players;
using DotaAnalyst.Server.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Tests.Server;

// The whole Steam login, offline: the browser side is played by the test client, and the server-to-Steam
// calls (discovery, check_authentication) are answered by ApiFactory's fake Steam.
public class AuthApiTests
{
    private const long MyAccountId = 99517715;
    private const string MySteamId64 = "76561198059783443";

    [Fact]
    public async Task Login_RedirectsToSteamWithOurCallback()
    {
        using var factory = new ApiFactory();
        var client = CreateBrowser(factory);

        var response = await client.GetAsync("/api/v1/auth/login?returnUrl=/matches/1");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("https://steamcommunity.com/openid/login", location.GetLeftPart(UriPartial.Path));
        var returnTo = new Uri(QueryHelpers.ParseQuery(location.Query)["openid.return_to"]!);
        Assert.Equal("http://localhost" + SteamAuthentication.CallbackPath, returnTo.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task FullLogin_CreatesSessionAndMeReturnsTheUser()
    {
        using var factory = new ApiFactory();
        factory.SteamProfiles.Profile = new PlayerProfile("Andreo", new Uri("https://avatars.steamstatic.com/a_full.jpg"));
        var client = CreateBrowser(factory);

        var afterLogin = await LogInAsync(client, "/matches/1", $"https://steamcommunity.com/openid/id/{MySteamId64}");

        // Back on the page where the login started, with the session cookie.
        Assert.Equal(HttpStatusCode.Found, afterLogin.StatusCode);
        Assert.Equal("/matches/1", afterLogin.Headers.Location!.OriginalString);
        var sessionCookie = Assert.Single(afterLogin.Headers.GetValues("Set-Cookie"), c => c.StartsWith(SteamAuthentication.CookieName));
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        // Persistent: survives closing the browser (a cookie without expires dies with the browser session).
        Assert.Contains("expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(MyAccountId, json.GetProperty("accountId").GetInt64());
        // A string: too big for JavaScript numbers.
        Assert.Equal(MySteamId64, json.GetProperty("steamId64").GetString());
        Assert.Equal("Andreo", json.GetProperty("personaName").GetString());
        Assert.Equal("https://avatars.steamstatic.com/a_full.jpg", json.GetProperty("avatarUrl").GetString());
    }

    [Fact]
    public async Task Login_SteamDoesNotConfirm_NoSession()
    {
        using var factory = new ApiFactory();
        factory.Steam.LoginIsValid = false;
        var client = CreateBrowser(factory);

        var afterLogin = await LogInAsync(client, "/", $"https://steamcommunity.com/openid/id/{MySteamId64}");

        Assert.Equal(SteamAuthentication.LoginFailedPath, afterLogin.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Theory]
    [InlineData("https://steamcommunity.com/openid/id/123")] // not a SteamID64
    [InlineData("https://evil.example/openid/id/76561198059783443")]
    public async Task Login_UnexpectedSteamIdentity_NoSession(string identity)
    {
        using var factory = new ApiFactory();
        var client = CreateBrowser(factory);

        var afterLogin = await LogInAsync(client, "/", identity);

        Assert.Equal(SteamAuthentication.LoginFailedPath, afterLogin.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("")]
    public async Task Login_ReturnUrlOutsideTheSite_GoesBackToHome(string returnUrl)
    {
        using var factory = new ApiFactory();
        var client = CreateBrowser(factory);

        var afterLogin = await LogInAsync(client, returnUrl, $"https://steamcommunity.com/openid/id/{MySteamId64}");

        Assert.Equal("/", afterLogin.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_KeysUnavailable_GoesToLoginFailedInsteadOf500()
    {
        // Like the database (where the keys live) being down: encrypting the login state fails.
        using var factory = new ApiFactory();
        using var brokenKeys = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IDataProtectionProvider>(new FailingDataProtectionProvider())));
        var client = brokenKeys.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/v1/auth/login?returnUrl=/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SteamAuthentication.LoginFailedPath, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Me_WithoutSession_Returns401Problem()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/v1/me");

        PlayersApiTests.AssertProblem(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_SteamProfileUnavailable_StillReturnsTheUser()
    {
        using var factory = new ApiFactory();
        factory.SteamProfiles.Fail = true;
        var client = CreateBrowser(factory);
        await LogInAsync(client, "/", $"https://steamcommunity.com/openid/id/{MySteamId64}");

        var json = JsonDocument.Parse(await client.GetStringAsync("/api/v1/me")).RootElement;

        Assert.Equal(MyAccountId, json.GetProperty("accountId").GetInt64());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("personaName").ValueKind);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        using var factory = new ApiFactory();
        var client = CreateBrowser(factory);
        await LogInAsync(client, "/", $"https://steamcommunity.com/openid/id/{MySteamId64}");

        var logout = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutSession_IsFine()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed class FailingDataProtectionProvider : IDataProtectionProvider, IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext) => throw new CryptographicException("The key ring is unavailable.");

        public byte[] Unprotect(byte[] protectedData) => throw new CryptographicException("The key ring is unavailable.");
    }

    // A "browser": keeps cookies, and doesn't follow redirects so each step can be checked.
    private static HttpClient CreateBrowser(ApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    // Starts the login, then comes back from "Steam" as if the user had logged in with the given identity.
    private static async Task<HttpResponseMessage> LogInAsync(HttpClient client, string returnUrl, string identity)
    {
        var challenge = await client.GetAsync($"/api/v1/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        var steamQuery = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        var returnTo = steamQuery["openid.return_to"].ToString();

        var answer = new Dictionary<string, string?>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "id_res",
            ["openid.op_endpoint"] = "https://steamcommunity.com/openid/login",
            ["openid.claimed_id"] = identity,
            ["openid.identity"] = identity,
            ["openid.return_to"] = returnTo,
            ["openid.response_nonce"] = $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}abc",
            ["openid.assoc_handle"] = "1234567890",
            ["openid.signed"] = "signed,op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle",
            ["openid.sig"] = "fake-signature",
        };

        // Steam sends the browser back to return_to (which already carries our state) plus its answer.
        return await client.GetAsync(QueryHelpers.AddQueryString(returnTo, answer));
    }
}

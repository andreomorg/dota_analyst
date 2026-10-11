using AspNet.Security.OpenId.Steam;
using DotaAnalyst.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace DotaAnalyst.Server.Controllers.V1;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Starts the Steam login.</summary>
    /// <remarks>
    /// Open it in the browser (a link, not fetch): it redirects to Steam's login page, where the user signs in
    /// with password or the QR code in the Steam app. After a successful login Steam sends the browser back and
    /// it lands on `returnUrl` with the session cookie; on failure it lands on `/?login=failed`.
    /// </remarks>
    /// <param name="returnUrl">Path of this site to come back to (e.g. `/matches/123`). Other sites are ignored (goes to `/`).</param>
    /// <response code="302">Redirect to Steam (or to `/?login=failed` when the login can't start).</response>
    [HttpGet("login")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Login(string? returnUrl)
    {
        // Only addresses of this site: otherwise our login could be used to send people to a fake site
        // that looks trustworthy because the link started at dotaanalyst ("open redirect").
        var redirectUri = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectUri,
            // A persistent cookie (with an expiry date) survives closing the browser; without this flag it
            // would be a "session cookie" and SessionLifetime would never apply.
            IsPersistent = true,
        };

        try
        {
            // Run here (not as a ChallengeResult) so a failure can be caught: starting the login encrypts its
            // state with the Data Protection keys, which live in the database.
            await HttpContext.ChallengeAsync(SteamAuthenticationDefaults.AuthenticationScheme, properties);
            return new EmptyResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // E.g. the database (and so the keys) is down: the login is unavailable, but the rest of the site works.
            logger.LogError(ex, "Could not start the Steam login");
            return Redirect(SteamAuthentication.LoginFailedPath);
        }
    }

    // POST, not GET: a link or image on another site can't log anyone out (and the SameSite=Lax cookie is
    // not even sent on POSTs coming from other sites). Logging out without a session is fine too.
    /// <summary>Ends the session.</summary>
    /// <response code="204">Logged out (also when there was no session).</response>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

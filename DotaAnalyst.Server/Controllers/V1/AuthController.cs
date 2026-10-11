using AspNet.Security.OpenId.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace DotaAnalyst.Server.Controllers.V1;

[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    // Starts the Steam login: the browser navigates here (a link, not fetch) and is redirected to Steam's
    // login page (password or QR code). After a successful login it comes back to returnUrl.
    [HttpGet("login")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Login(string? returnUrl)
    {
        // Only addresses of this site: otherwise our login could be used to send people to a fake site
        // that looks trustworthy because the link started at dotaanalyst ("open redirect").
        var redirectUri = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return Challenge(new AuthenticationProperties { RedirectUri = redirectUri }, SteamAuthenticationDefaults.AuthenticationScheme);
    }

    // POST, not GET: a link or image on another site can't log anyone out (and the SameSite=Lax cookie is
    // not even sent on POSTs coming from other sites). Logging out without a session is fine too.
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }
}

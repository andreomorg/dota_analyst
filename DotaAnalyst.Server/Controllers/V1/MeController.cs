using DotaAnalyst.Application.Players;
using DotaAnalyst.Server.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotaAnalyst.Server.Controllers.V1;

// The logged-in user. Without a session the cookie handler answers 401 (see SteamAuthentication).
[ApiController]
[Authorize]
[Route("api/v1/me")]
public class MeController(GetUserProfileHandler getUserProfile) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserProfile>> Get(CancellationToken cancellationToken)
    {
        if (!CurrentUser.TryGetAccountId(User, out var accountId))
        {
            // A session cookie without a valid account ID (should never happen): treat as not logged in.
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Not logged in");
        }

        return Ok(await getUserProfile.HandleAsync(accountId, cancellationToken));
    }
}

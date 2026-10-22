using DotaAnalyst.Application.Catalogs;
using Microsoft.AspNetCore.Mvc;

namespace DotaAnalyst.Server.Controllers.V1;

[ApiController]
[Route("api/v1/heroes")]
public sealed class HeroProfilesController(GetHeroProfileHandler handler) : ControllerBase
{
    /// <summary>Nominal hero configuration and Portuguese factual description, with source build.</summary>
    /// <param name="heroId">Positive hero identifier.</param>
    /// <param name="at">Optional match-start timestamp for historical game data; omitted means the latest known build.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    /// <response code="200">Profile; missing components remain null and current fallback is explicit.</response>
    /// <response code="400">Invalid hero identifier or timestamp.</response>
    /// <response code="404">No known hero profile.</response>
    [HttpGet("{heroId}/profile")]
    [ProducesResponseType<HeroProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HeroProfileResponse>> GetProfile(int heroId, [FromQuery] DateTimeOffset? at,
        CancellationToken cancellationToken)
    {
        if (heroId <= 0) return Problem(statusCode: 400, title: "Invalid hero ID");
        return await handler.HandleAsync(heroId, at, cancellationToken) is { } result ? Ok(result)
            : Problem(statusCode: 404, title: "Unknown hero");
    }
}

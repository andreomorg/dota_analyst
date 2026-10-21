using DotaAnalyst.Application.Catalogs;
using Microsoft.AspNetCore.Mvc;

namespace DotaAnalyst.Server.Controllers.V1;

[ApiController]
[Route("api/v1/heroes")]
public sealed class HeroesController(GetHeroKitHandler handler) : ControllerBase
{
    /// <summary>The hero's abilities, talents and facets in the constants patch.</summary>
    /// <param name="heroId">Numeric hero identifier.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up.</param>
    /// <response code="200">The kit; empty lists when its catalog is unavailable.</response>
    /// <response code="404">Unknown hero.</response>
    [HttpGet("{heroId:int}/kit")]
    [ProducesResponseType<HeroKitResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HeroKitResponse>> GetKit(int heroId, CancellationToken cancellationToken) =>
        await handler.HandleAsync(heroId, cancellationToken) is { } kit ? Ok(kit)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown hero");
}

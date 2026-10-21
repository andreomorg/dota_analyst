using DotaAnalyst.Application.Catalogs;
using Microsoft.AspNetCore.Mvc;

namespace DotaAnalyst.Server.Controllers.V1;

[ApiController]
[Route("api/v1/items")]
public sealed class ItemsController(GetItemHandler handler) : ControllerBase
{
    /// <summary>The complete item from the game constants.</summary>
    /// <param name="itemKey">Internal key, for example black_king_bar.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up.</param>
    /// <response code="200">Item details.</response>
    /// <response code="404">Item not found in the available catalog.</response>
    [HttpGet("{itemKey}")]
    [ProducesResponseType<ItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ItemResponse>> Get(string itemKey, CancellationToken cancellationToken) =>
        await handler.HandleAsync(itemKey, cancellationToken) is { } item ? Ok(item)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown item");
}

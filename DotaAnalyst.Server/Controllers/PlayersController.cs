using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Players;
using Microsoft.AspNetCore.Mvc;

namespace DotaAnalyst.Server.Controllers;

[ApiController]
[Route("api/players")]
public class PlayersController(
    GetRecentMatchesHandler getRecentMatches,
    ILogger<PlayersController> logger) : ControllerBase
{
    // No ":long" route constraint on purpose: a non-numeric or too large ID must reach the action
    // and get a 400 from model binding, instead of not matching the route at all.
    [HttpGet("{id}/matches")]
    [ProducesResponseType<IReadOnlyList<MatchSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<MatchSummary>>> GetRecentMatches(
        long id, CancellationToken cancellationToken)
    {
        if (!AccountId.TryCreate(id, out var accountId))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid account ID",
                detail: "Use a Dota account ID or a SteamID64.");
        }

        try
        {
            return Ok(await getRecentMatches.HandleAsync(accountId, cancellationToken));
        }
        catch (DataSourceUnavailableException ex)
        {
            logger.LogError(ex, "Could not load matches for account {AccountId}", accountId);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "OpenDota unavailable",
                detail: "Could not load matches from OpenDota. Please try again in a few minutes.");
        }
    }
}

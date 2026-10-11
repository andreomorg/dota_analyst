using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;
using Microsoft.AspNetCore.Mvc;

namespace DotaAnalyst.Server.Controllers.V1;

// No ":long" route constraints on purpose: a non-numeric or too large ID must reach the action
// and get a 400 from model binding, instead of not matching the route at all.
[ApiController]
[Route("api/v1/players")]
public class PlayersController(
    GetRecentMatchesHandler getRecentMatches,
    GetMatchAnalysisHandler getMatchAnalysis,
    ILogger<PlayersController> logger) : ControllerBase
{
    /// <summary>Recent matches of a player.</summary>
    /// <remarks>
    /// The last 20 matches from OpenDota, with hero names. A private Steam/Dota profile returns an empty list.
    /// Searches that work are recorded (only the account ID and the time; nothing about the visitor).
    /// </remarks>
    /// <param name="id">Dota account ID (e.g. 99517715) or SteamID64 (e.g. 76561198059783443).</param>
    /// <param name="cancellationToken">Cancelled when the client gives up.</param>
    /// <response code="200">The matches, most recent first.</response>
    /// <response code="400">The ID is not a valid account ID or SteamID64.</response>
    /// <response code="502">OpenDota is unavailable.</response>
    [HttpGet("{id}/matches")]
    [ProducesResponseType<IReadOnlyList<MatchSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<MatchSummary>>> GetRecentMatches(
        long id, CancellationToken cancellationToken)
    {
        if (!AccountId.TryCreate(id, out var accountId))
        {
            return InvalidAccountId();
        }

        try
        {
            return Ok(await getRecentMatches.HandleAsync(accountId, cancellationToken));
        }
        catch (DataSourceUnavailableException ex)
        {
            logger.LogError(ex, "Could not load matches for account {AccountId}", accountId);
            return OpenDotaUnavailable();
        }
    }

    /// <summary>Analysis of a match from the point of view of a player.</summary>
    /// <remarks>
    /// Both teams, plus the player's performance compared to other players of the same hero (strengths and
    /// points to improve). Matches already analyzed (with a known result) are saved in our database, so they usually
    /// still work when OpenDota is down.
    /// </remarks>
    /// <param name="id">Dota account ID or SteamID64 of the player whose point of view is used.</param>
    /// <param name="matchId">Dota match ID.</param>
    /// <param name="cancellationToken">Cancelled when the client gives up.</param>
    /// <response code="200">The analysis. `performance` is null when the player is not in the match or has an anonymous profile.</response>
    /// <response code="400">Invalid account ID or match ID.</response>
    /// <response code="404">The match doesn't exist.</response>
    /// <response code="502">OpenDota is unavailable.</response>
    [HttpGet("{id}/matches/{matchId}")]
    [ProducesResponseType<MatchAnalysis>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MatchAnalysis>> GetMatchAnalysis(
        long id, long matchId, CancellationToken cancellationToken)
    {
        if (!AccountId.TryCreate(id, out var accountId))
        {
            return InvalidAccountId();
        }

        if (!MatchId.TryCreate(matchId, out var validMatchId))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid match ID",
                detail: "The match ID must be a positive number.");
        }

        try
        {
            var analysis = await getMatchAnalysis.HandleAsync(validMatchId, accountId, cancellationToken);
            return analysis is null
                ? Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Match not found",
                    detail: $"Match {matchId} was not found.")
                : Ok(analysis);
        }
        catch (DataSourceUnavailableException ex)
        {
            logger.LogError(ex, "Could not load match {MatchId} for account {AccountId}", matchId, accountId);
            return OpenDotaUnavailable();
        }
    }

    private ObjectResult InvalidAccountId() => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid account ID",
        detail: "Use a Dota account ID or a SteamID64.");

    private ObjectResult OpenDotaUnavailable() => Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "OpenDota unavailable",
        detail: "Could not load data from OpenDota. Please try again in a few minutes.");
}

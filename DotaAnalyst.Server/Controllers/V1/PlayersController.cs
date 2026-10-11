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

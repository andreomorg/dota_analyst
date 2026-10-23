using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Server.Controllers.Internal;

// Machine-to-machine contract. Steam/session authentication and the public v1 API remain independent.
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/internal/coach")]
public sealed class CoachingController(ReviewCoachingHandler handler, GetSituatedDecisionsHandler situated,
    IOptions<CoachMlOptions> settings) : ControllerBase
{
    private static readonly System.Text.Json.JsonSerializerOptions V1DecisionJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>The v5 decision context (v1 scenes + the team-visible situation at each anchor) of one hero in a kept
    /// replay, for the local coach corpus.</summary>
    [HttpGet("situated-decisions/{matchId}/{heroId}")]
    public async Task<IActionResult> SituatedDecisions(long matchId, int heroId, CancellationToken cancellationToken,
        [FromQuery] bool includeSignals = false)
    {
        var options = settings.Value;
        if (!options.Enabled) return NotFound();
        if (!MachineKey.IsValid(Request, options.ApiKey)) return Unauthorized();
        if (!MatchId.TryCreate(matchId, out var id) || heroId <= 0)
            return Problem(statusCode: 400, title: "Invalid match or hero ID.");
        try
        {
            var result = await situated.HandleAsync(id, heroId, cancellationToken, includeSignals);
            if (result is null) return NotFound();
            // The decision block stays byte-compatible with the v1 decision documents (numeric enums), which the
            // existing projection reads; heroes and situations follow the API's text enums (v5 contract).
            return Ok(new
            {
                result.SchemaVersion,
                Decision = System.Text.Json.JsonSerializer.SerializeToElement(result.Decision, V1DecisionJson),
                result.Heroes,
                result.Situations,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.Text.Json.JsonException
            or NotSupportedException)
        {
            // No metrics/role/timeline for the hero, or an unreadable document: the context cannot be built.
            return Problem(statusCode: 422, title: "The kept replay lacks what the coaching context needs.");
        }
    }

    /// <summary>Provides bounded evidence from an already stored replay to the Python worker.</summary>
    [HttpGet("contexts/{matchId}/{heroId}")]
    public Task<IActionResult> Context(long matchId, int heroId, CancellationToken cancellationToken)
        => Execute(matchId, heroId, false, cancellationToken);

    /// <summary>Requests an experimental review from the Python service.</summary>
    [HttpPost("reviews/{matchId}/{heroId}")]
    public Task<IActionResult> Review(long matchId, int heroId, CancellationToken cancellationToken)
        => Execute(matchId, heroId, true, cancellationToken);

    private async Task<IActionResult> Execute(long matchId, int heroId, bool review, CancellationToken cancellationToken)
    {
        var options = settings.Value;
        if (!options.Enabled) return NotFound();
        if (!MachineKey.IsValid(Request, options.ApiKey)) return Unauthorized();
        if (!MatchId.TryCreate(matchId, out var id) || heroId <= 0)
            return Problem(statusCode: 400, title: "Invalid match or hero ID.");
        try
        {
            object? result = review ? await handler.ReviewAsync(id, heroId, cancellationToken)
                : await handler.GetContextAsync(id, heroId, cancellationToken);
            return result is null ? NotFound() : Ok(result);
        }
        catch (HttpRequestException) { return Problem(statusCode: 502, title: "Coach service unavailable."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Problem(statusCode: 504, title: "Coach service timed out."); }
        catch (InvalidDataException) { return Problem(statusCode: 502, title: "Invalid coaching evidence or service response."); }
        catch (System.Text.Json.JsonException) { return Problem(statusCode: 502, title: "Invalid coaching document."); }
    }
}

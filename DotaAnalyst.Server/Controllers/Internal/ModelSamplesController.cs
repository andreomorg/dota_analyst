using DotaAnalyst.Application.Models;
using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Server.Controllers.Internal;

// Machine-to-machine: training examples for the local prediction models (coach-ml), read from kept replays. Same
// machine key and switch as the coach routes; outside the public API and its documentation.
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/internal")]
public sealed class ModelSamplesController(
    GetModelSamplesHandler samples, RefreshReplayHandler refresh, GetPositionEstimatesHandler positions,
    IOptions<CoachMlOptions> settings) : ControllerBase
{
    /// <summary>The position estimator against the real positions of one kept replay (for measuring it).</summary>
    [HttpGet("position-estimates/{matchId}")]
    public Task<IActionResult> PositionEstimates(long matchId, CancellationToken cancellationToken) =>
        Respond(matchId, async id =>
        {
            var result = await positions.HandleAsync(id, cancellationToken);
            return (result.Status, result.Estimates);
        });

    /// <summary>Parses a match's replay again so its kept document has the newest sections (never downgrades a saved
    /// analysis: a gone replay changes nothing).</summary>
    [HttpPost("replays/{matchId}/refresh")]
    public Task<IActionResult> Refresh(long matchId, CancellationToken cancellationToken) =>
        Replay(matchId, id => refresh.HandleAsync(id, cancellationToken));

    /// <summary>Downloads a match's replay into the parser's cache ahead of a refresh (a batch keeps the parse slots
    /// parsing instead of waiting on Valve). Saves nothing.</summary>
    [HttpPost("replays/{matchId}/prefetch")]
    public Task<IActionResult> Prefetch(long matchId, CancellationToken cancellationToken) =>
        Replay(matchId, id => refresh.PrefetchAsync(id, cancellationToken));

    private async Task<IActionResult> Replay(long matchId, Func<MatchId, Task<RefreshReplayResult>> handle)
    {
        var options = settings.Value;
        if (!options.Enabled) return NotFound();
        if (!MachineKey.IsValid(Request, options)) return Unauthorized();
        if (!MatchId.TryCreate(matchId, out var id)) return Problem(statusCode: 400, title: "Invalid match ID.");
        try
        {
            var result = await handle(id);
            return Ok(new { status = result });
        }
        catch (DotaAnalyst.Application.Abstractions.StorageUnavailableException)
        {
            return Problem(statusCode: 503, title: "The database is unavailable.");
        }
    }

    /// <summary>The death-risk examples of one kept replay.</summary>
    [HttpGet("death-risk/samples/{matchId}")]
    public Task<IActionResult> DeathRisk(long matchId, CancellationToken cancellationToken) =>
        Execute(matchId, id => samples.DeathRiskAsync(id, cancellationToken));

    /// <summary>The next-region examples (where the hero was 30 s later) of one kept replay.</summary>
    [HttpGet("next-region/samples/{matchId}")]
    public Task<IActionResult> NextRegion(long matchId, CancellationToken cancellationToken) =>
        Execute(matchId, id => samples.NextRegionAsync(id, cancellationToken));

    private Task<IActionResult> Execute<T>(long matchId, Func<MatchId, Task<ModelSamplesResult<T>>> handle) =>
        Respond(matchId, async id =>
        {
            var result = await handle(id);
            return (result.Status, (object?)result.Samples);
        });

    private async Task<IActionResult> Respond<T>(long matchId, Func<MatchId, Task<(ModelSamplesStatus Status, T? Body)>> handle)
    {
        var options = settings.Value;
        if (!options.Enabled) return NotFound();
        if (!MachineKey.IsValid(Request, options)) return Unauthorized();
        if (!MatchId.TryCreate(matchId, out var id)) return Problem(statusCode: 400, title: "Invalid match ID.");
        try
        {
            var result = await handle(id);
            return result.Status switch
            {
                ModelSamplesStatus.Found => Ok(result.Body),
                ModelSamplesStatus.Outdated => Problem(statusCode: 422, title: "The kept replay lacks what the samples need."),
                _ => Problem(statusCode: 404, title: "No kept replay for this match."),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        {
            return Problem(statusCode: 422, title: "The kept replay is unreadable.");
        }
    }
}

using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Server.Controllers.Internal;

// Machine-to-machine: the facts of a kept replay around one hero at one second (work/data-flow-audit D18). Same machine
// key and switch as the other internal routes; outside the public API and its documentation. Never parses or refreshes.
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/internal/replay-facts")]
public sealed class ReplayFactsController(GetReplayFactsHandler facts, IOptions<CoachMlOptions> settings,
    IOptions<MaintenanceOptions> maintenanceSettings) : ControllerBase
{
    /// <summary>The hero's state at the second, the events of the window before it and, with includeFinal at the end of
    /// the match, its whole-match facts.</summary>
    [HttpGet("{matchId}")]
    public async Task<IActionResult> Get(long matchId, [FromQuery] int heroId, [FromQuery] int seconds,
        [FromQuery] int windowSeconds = 30, [FromQuery] bool includeFinal = false, CancellationToken cancellationToken = default)
    {
        var options = settings.Value;
        var maintenance = maintenanceSettings.Value;
        if (!options.Enabled && !maintenance.Enabled) return NotFound();
        if (!(options.Enabled && MachineKey.IsValid(Request, options.ApiKey)) &&
            !(maintenance.Enabled && MachineKey.IsValid(Request, maintenance.ApiKey))) return Unauthorized();
        if (!MatchId.TryCreate(matchId, out var id)) return Problem(statusCode: 400, title: "Invalid match ID.");
        try
        {
            var result = await facts.HandleAsync(id, heroId, seconds, windowSeconds, includeFinal, cancellationToken);
            return result.Status switch
            {
                ReplayFactsStatus.Found => Ok(result.Facts),
                ReplayFactsStatus.Invalid => Problem(statusCode: 400, title: "Invalid query.", detail: result.Problem),
                _ => Problem(statusCode: 404, title: "No kept replay for this match."),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        {
            return Problem(statusCode: 422, title: "The kept replay is unreadable.");
        }
        catch (DotaAnalyst.Application.Abstractions.StorageUnavailableException)
        {
            return Problem(statusCode: 503, title: "The database is unavailable.");
        }
    }
}

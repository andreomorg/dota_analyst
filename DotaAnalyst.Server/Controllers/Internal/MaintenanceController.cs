using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Server.Controllers.Internal;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/internal")]
public sealed class MaintenanceController(RefreshReplayHandler refresh, IOptions<CoachMlOptions> settings,
    IOptions<MaintenanceOptions> maintenanceSettings) : ControllerBase
{
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
        var maintenance = maintenanceSettings.Value;
        if (!options.Enabled && !maintenance.Enabled) return NotFound();
        if (!(options.Enabled && MachineKey.IsValid(Request, options.ApiKey)) &&
            !(maintenance.Enabled && MachineKey.IsValid(Request, maintenance.ApiKey))) return Unauthorized();
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

}

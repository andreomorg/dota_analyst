using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Replays;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.Coaching;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Server.Controllers.Internal;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/internal/gold-facts")]
public sealed class GoldFactsController(IArchivedReplaySource replays, IOptions<CoachMlOptions> settings,
    IOptions<MaintenanceOptions> maintenanceSettings) : ControllerBase
{
    // The handler has only the archive dependency; constructing it here avoids changing the shared DI registry.
    private readonly GetGoldFactsHandler handler = new(replays);

    [HttpGet("{matchId}")]
    public async Task<IActionResult> Get(long matchId, [FromQuery] int heroId, [FromQuery] double seconds,
        [FromQuery] int windowSeconds = 30, CancellationToken cancellationToken = default)
    {
        var options = settings.Value;
        var maintenance = maintenanceSettings.Value;
        if (!options.Enabled && !maintenance.Enabled) return NotFound();
        if (!(options.Enabled && MachineKey.IsValid(Request, options.ApiKey)) &&
            !(maintenance.Enabled && MachineKey.IsValid(Request, maintenance.ApiKey))) return Unauthorized();
        if (!MatchId.TryCreate(matchId, out var id)) return Problem(statusCode: 400, title: "Invalid match ID.");
        try
        {
            var result = await handler.HandleAsync(id, heroId, seconds, windowSeconds, cancellationToken);
            return result.Status switch
            {
                ReplayFactsStatus.Found => Ok(result.Facts),
                ReplayFactsStatus.Invalid => Problem(statusCode: 400, title: "Invalid query.", detail: result.Problem),
                _ => Problem(statusCode: 404, title: "No kept replay for this match."),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or NotSupportedException or OverflowException)
        {
            return Problem(statusCode: 422, title: "The kept gold observations are unreadable.");
        }
        catch (StorageUnavailableException)
        {
            return Problem(statusCode: 503, title: "The database is unavailable.");
        }
    }
}

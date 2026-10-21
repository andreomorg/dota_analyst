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
public sealed class CoachingController(ReviewCoachingHandler handler, IOptions<CoachMlOptions> settings) : ControllerBase
{
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
        if (!MachineKey.IsValid(Request, options)) return Unauthorized();
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

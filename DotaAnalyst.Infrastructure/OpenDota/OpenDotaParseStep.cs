using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.OpenDota;

public sealed class OpenDotaParseStep(IOpenDotaParseRequestRepository requests, IOpenDotaParseClient client,
    IReplayAnalyzer analyzer, IMatchRepository matches, IOptions<OpenDotaParsingOptions> configuration, TimeProvider clock)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var request = await requests.ClaimAsync(clock.GetUtcNow(), TimeSpan.FromMinutes(2), cancellationToken);
        if (request is null) return false;
        var options = configuration.Value;
        var now = clock.GetUtcNow();
        MatchId.TryCreate(request.MatchId, out var id);
        var next = request with { UpdatedAt = now };
        try
        {
            if (request.State == "submitting")
                next = next with { State = "refreshing", NextAttemptAt = now.AddMinutes(options.ResumeMinutes), LastErrorCode = "outcomeUnknown" };
            else if (request.State == "refreshing" || request.JobId is null && request.State == "deferred")
            {
                var match = await analyzer.RefreshMatchDetailsAsync(id, cancellationToken);
                if (match?.IsParsed == true)
                {
                    await matches.SaveAsync(match, now, cancellationToken);
                    next = next with { State = "ready", LastErrorCode = null };
                }
                else
                {
                    if (request.EnrichmentOnly)
                    {
                        await requests.CompleteAsync(next with
                        {
                            State = "refreshing",
                            LastErrorCode = "notParsedYet",
                            NextAttemptAt = now.AddMinutes(options.ResumeMinutes)
                        }, cancellationToken);
                        return true;
                    }
                    var reset = now - request.RequestedAt >= TimeSpan.FromHours(options.RetryHours);
                    next = next with
                    {
                        State = "queued",
                        JobId = null,
                        PollAttempts = 0,
                        SubmitAttempts = reset ? 0 : request.SubmitAttempts,
                        RequestedAt = reset ? now : request.RequestedAt,
                        NextAttemptAt = request.SubmitAttempts >= options.MaxSubmitAttempts && !reset
                            ? request.RequestedAt.AddHours(options.RetryHours) : now.AddMinutes(options.ResumeMinutes),
                        LastErrorCode = "notParsedYet"
                    };
                }
            }
            else if (request.JobId is { } jobId)
            {
                var pending = await client.IsPendingAsync(jobId, cancellationToken);
                var count = request.PollAttempts + 1;
                next = next with
                {
                    State = pending ? "processing" : "refreshing",
                    PollAttempts = count,
                    LastErrorCode = null,
                    NextAttemptAt = pending
                        ? now + (now - request.RequestedAt >= TimeSpan.FromMinutes(options.ActiveMinutes)
                            ? TimeSpan.FromMinutes(options.ResumeMinutes)
                            : TimeSpan.FromSeconds(Math.Min(options.MaxPollSeconds, count * options.PollSeconds))) : now
                };
            }
            else
            {
                var saved = await matches.FindAsync(id, cancellationToken);
                if (saved?.Match.IsParsed == true || request.EnrichmentOnly)
                {
                    await requests.CompleteAsync(next with { State = "refreshing", NextAttemptAt = now }, cancellationToken);
                    return true;
                }
                if (request.SubmitAttempts >= options.MaxSubmitAttempts)
                {
                    await requests.CompleteAsync(next with
                    {
                        State = "refreshing",
                        LastErrorCode = "attemptLimit",
                        NextAttemptAt = request.RequestedAt.AddHours(options.RetryHours) > now
                            ? request.RequestedAt.AddHours(options.RetryHours) : now
                    }, cancellationToken);
                    return true;
                }
                // Persist intent BEFORE POST. Interrupted/cancelled sends are reconciled without immediate retry.
                next = next with { State = "submitting", SubmitAttempts = request.SubmitAttempts + 1 };
                if (!await requests.CheckpointAsync(next, cancellationToken)) return true;
                var job = await client.SubmitAsync(id, cancellationToken);
                next = next with { State = "processing", JobId = job, LastErrorCode = null, NextAttemptAt = now.AddSeconds(options.PollSeconds) };
            }
        }
        catch (OpenDotaParseException ex)
        {
            next = next with
            {
                State = ex.Code is "invalidRequest" or "accessDenied" ? "failed"
                    : ex.OutcomeUnknown || ex.Code == "jobMissing" ? "refreshing" : "deferred",
                LastErrorCode = ex.Code,
                NextAttemptAt = now.AddMinutes(options.ResumeMinutes)
            };
        }
        catch (DataSourceUnavailableException)
        {
            next = next with { State = "refreshing", LastErrorCode = "serviceUnavailable", NextAttemptAt = now.AddMinutes(options.ResumeMinutes) };
        }
        // Cancellation/storage failures leave the durable lease/checkpoint to be recovered.
        await requests.CompleteAsync(next with { UpdatedAt = clock.GetUtcNow() }, cancellationToken);
        return true;
    }
}

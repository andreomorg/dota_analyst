using DotaAnalyst.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DotaAnalyst.Infrastructure.Persistence.OpenDota;

public sealed class OpenDotaParseRequestRepository(
    IDbContextFactory<AppDbContext> factory, StorageGuard guard) : IOpenDotaParseRequestRepository
{
    public Task<OpenDotaParseRequest> EnsureAsync(long matchId, DateTimeOffset now, CancellationToken cancellationToken,
        bool enrichmentOnly = false) =>
        guard.RunAsync("registering OpenDota parse", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            now = now.ToUniversalTime();
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO opendota_parse_requests
                    (match_id, state, submit_attempts, poll_attempts, requested_at, updated_at, next_attempt_at, enrichment_only)
                VALUES ({matchId}, {(enrichmentOnly ? "refreshing" : "queued")}, 0, 0, {now}, {now}, {now}, {enrichmentOnly})
                ON CONFLICT (match_id) DO UPDATE SET
                    state = 'refreshing', enrichment_only = TRUE, next_attempt_at = EXCLUDED.next_attempt_at
                WHERE {enrichmentOnly} AND opendota_parse_requests.state IN ('ready', 'failed', 'unavailable')
                    AND opendota_parse_requests.updated_at < {now.AddMinutes(-15)}
                """, cancellationToken);
            return ToRequest(await db.Set<OpenDotaParseRequestRecord>().AsNoTracking()
                .SingleAsync(row => row.MatchId == matchId, cancellationToken));
        });

    public Task<OpenDotaParseRequest?> ClaimAsync(DateTimeOffset now, TimeSpan lease, CancellationToken cancellationToken) =>
        guard.RunAsync<OpenDotaParseRequest?>("claiming OpenDota parse", async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            now = now.ToUniversalTime();
            var token = Guid.NewGuid();
            var until = now + lease;
            // The lock, selection and update are a single statement, not a transaction around HTTP.
            var rows = await db.Set<OpenDotaParseRequestRecord>().FromSql($"""
                UPDATE opendota_parse_requests SET lease_token = {token}, lease_until = {until}
                WHERE match_id = (
                    SELECT match_id FROM opendota_parse_requests
                    WHERE state NOT IN ('ready', 'failed', 'unavailable') AND next_attempt_at <= {now}
                        AND (lease_until IS NULL OR lease_until <= {now})
                    ORDER BY next_attempt_at, match_id FOR UPDATE SKIP LOCKED LIMIT 1
                ) RETURNING *
                """).AsNoTracking().ToListAsync(cancellationToken);
            return rows.Count == 0 ? null : ToRequest(rows[0]);
        });

    public Task<bool> CompleteAsync(OpenDotaParseRequest request, CancellationToken cancellationToken) =>
        WriteAsync(request, releaseLease: true, cancellationToken);

    public Task<bool> CheckpointAsync(OpenDotaParseRequest request, CancellationToken cancellationToken) =>
        WriteAsync(request, releaseLease: false, cancellationToken);

    private Task<bool> WriteAsync(OpenDotaParseRequest request, bool releaseLease, CancellationToken cancellationToken) =>
        guard.RunAsync("updating OpenDota parse", async () =>
        {
            if (request.LeaseToken is null) return false;
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            return await db.Database.ExecuteSqlAsync($"""
                UPDATE opendota_parse_requests SET state = {request.State}, job_id = {request.JobId},
                    submit_attempts = {request.SubmitAttempts}, poll_attempts = {request.PollAttempts},
                    requested_at = {request.RequestedAt.ToUniversalTime()}, updated_at = {request.UpdatedAt.ToUniversalTime()},
                    next_attempt_at = {request.NextAttemptAt.ToUniversalTime()}, last_error_code = {request.LastErrorCode},
                    lease_token = {(releaseLease ? null : request.LeaseToken)},
                    lease_until = {(releaseLease ? null : request.LeaseUntil)}
                WHERE match_id = {request.MatchId} AND lease_token = {request.LeaseToken}
                """, cancellationToken) == 1;
        });

    private static OpenDotaParseRequest ToRequest(OpenDotaParseRequestRecord row) => new(
        row.MatchId, row.State, row.JobId, row.SubmitAttempts, row.PollAttempts,
        row.RequestedAt, row.UpdatedAt, row.NextAttemptAt, row.LeaseUntil, row.LeaseToken, row.LastErrorCode, row.EnrichmentOnly);
}

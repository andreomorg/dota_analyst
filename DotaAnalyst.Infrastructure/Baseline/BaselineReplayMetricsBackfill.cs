using System.Text.Json;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Infrastructure.GameData;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Replays;
using DotaAnalyst.Infrastructure.ReplayParser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.Baseline;

// Gives the comparison base's rows collected before the terrain (MP8), vision and map objective metrics their
// HeroMetrics.Terrain, Vision, Objectives, Teleports and Plays, computed again from the replay document kept for
// the match: no replay is downloaded or parsed again, and only those parts of the stored metrics change (a part already
// there is kept). Only matches of a patch that has a map (7.41 on); rows whose match has no kept document stay without
// them (they still count for the other comparisons). Runs before each publish, like BaselineHistoryBackfill.
// Only a missing block is filled: a block already stored is never changed, not even a field inside it that was unknown
// then (e.g. Objectives.Counts). Changing how a block is computed therefore needs its old values removed first (local
// base: UPDATE baseline_metrics SET metrics = metrics - '<block>'), so the next run computes it again.
public class BaselineReplayMetricsBackfill(
    IDbContextFactory<AppDbContext> dbContextFactory,
    StorageGuard storageGuard,
    IReplayDocumentArchive archive,
    ILogger<BaselineReplayMetricsBackfill> logger)
{
    private static readonly JsonSerializerOptions DocumentOptions = new(JsonSerializerDefaults.Web);

    /// <returns>How many matches got metrics.</returns>
    /// <exception cref="Application.Abstractions.StorageUnavailableException">The database could not be read or written.</exception>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // The first moment a map covers: the release of the map's patch (Valve's patch notes kept with the application).
        if (EmbeddedGameMaps.Map is not { } map
            || PatchNotesList.Embedded.Where(patch => patch.Major == map.Patch).MinBy(patch => patch.ReleasedAt) is not { } first)
        {
            return 0;
        }

        var since = first.ReleasedAt;
        var matchIds = await storageGuard.RunAsync("finding comparison base rows without terrain, vision or objective metrics", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await db.Database
                .SqlQuery<long>($"""
                    SELECT DISTINCT match_id AS "Value" FROM baseline_metrics
                    WHERE start_time >= {since}
                      AND (coalesce(metrics -> 'terrain', 'null'::jsonb) = 'null'::jsonb
                        OR coalesce(metrics -> 'vision', 'null'::jsonb) = 'null'::jsonb
                        OR coalesce(metrics -> 'objectives', 'null'::jsonb) = 'null'::jsonb
                        OR coalesce(metrics -> 'teleports', 'null'::jsonb) = 'null'::jsonb
                        OR coalesce(metrics -> 'plays', 'null'::jsonb) = 'null'::jsonb)
                    """)
                .ToListAsync(cancellationToken);
        });

        var filled = 0;
        foreach (var id in matchIds)
        {
            if (!MatchId.TryCreate(id, out var matchId)
                || await archive.FindAsync(matchId, cancellationToken) is not { } document
                || ReadMetrics(matchId, document) is not { } metrics)
            {
                continue;
            }

            await storageGuard.RunAsync($"saving the terrain, vision and objective metrics of base match {id}", async () =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var rows = await db.BaselineMetrics.Where(row => row.MatchId == id).ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    if (metrics.GetValueOrDefault(row.HeroId) is not { } fresh
                        || JsonSerializer.Deserialize<StoredHeroMetrics>(row.Metrics, StoredReplayMappings.JsonOptions) is not { } stored
                        || (stored.Terrain is not null && stored.Vision is not null && stored.Objectives is not null && stored.Teleports is not null && stored.Plays is not null))
                    {
                        continue;
                    }

                    var updated = stored with
                    {
                        Terrain = stored.Terrain ?? fresh.Terrain,
                        Vision = stored.Vision ?? fresh.Vision,
                        Objectives = stored.Objectives ?? fresh.Objectives,
                        Teleports = stored.Teleports ?? fresh.Teleports,
                        Plays = stored.Plays ?? fresh.Plays,
                    };
                    row.Metrics = JsonSerializer.Serialize(updated, StoredReplayMappings.JsonOptions);
                }

                await db.SaveChangesAsync(cancellationToken);
            });
            filled++;
        }

        if (filled > 0)
        {
            logger.LogInformation("Filled the terrain, vision and objective metrics of {Matches} comparison base matches from their kept documents", filled);
        }

        return filled;
    }

    // The stored metrics of each hero of the match, from its kept document; null when it can't be read or no hero got
    // terrain, vision or objectives (another patch, no visibility).
    private Dictionary<int, StoredHeroMetrics>? ReadMetrics(MatchId matchId, string document)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ReplayParserMatchResponse>(document, DocumentOptions)
                ?? throw new InvalidDataException("Empty document.");
            var metrics = (response.ToReplayAnalysis(matchId).Metrics ?? [])
                .Where(hero => hero.Terrain is not null || hero.Vision is not null || hero.Objectives is not null || hero.Teleports is not null || hero.Plays is not null)
                .DistinctBy(hero => hero.HeroId)
                .ToDictionary(hero => hero.HeroId, StoredReplayMappings.ToStored);
            return metrics.Count == 0 ? null : metrics;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            logger.LogWarning(ex, "The kept document of base match {MatchId} can't be read", matchId.Value);
            return null;
        }
    }
}

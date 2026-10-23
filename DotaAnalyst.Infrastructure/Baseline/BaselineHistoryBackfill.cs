using System.Text.Json;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Stats;
using DotaAnalyst.Infrastructure.Persistence;
using DotaAnalyst.Infrastructure.Persistence.Baseline;
using DotaAnalyst.Infrastructure.ReplayParser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DotaAnalyst.Infrastructure.Baseline;

// Gives the comparison base's rows collected before the statistics per minute were kept their statistics, read again
// from the replay document kept for the match (the collector's machine keeps every document): no replay is downloaded
// or parsed again. Runs before each publish; once every row has them, it only costs one query. Rows whose match has
// no kept document stay without them (they still count for the other comparisons).
public class BaselineHistoryBackfill(
    IDbContextFactory<AppDbContext> dbContextFactory,
    StorageGuard storageGuard,
    IReplayDocumentArchive archive,
    ILogger<BaselineHistoryBackfill> logger)
{
    private static readonly JsonSerializerOptions DocumentOptions = new(JsonSerializerDefaults.Web);

    /// <returns>How many matches got their statistics.</returns>
    /// <exception cref="Application.Abstractions.StorageUnavailableException">The database could not be read or written.</exception>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var matchIds = await storageGuard.RunAsync("finding comparison base rows without statistics per minute", async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await db.BaselineMetrics
                .AsNoTracking()
                .Where(row => row.StatHistory == null)
                .Select(row => row.MatchId)
                .Distinct()
                .ToListAsync(cancellationToken);
        });

        var filled = 0;
        foreach (var id in matchIds)
        {
            if (!MatchId.TryCreate(id, out var matchId)
                || await archive.FindAsync(matchId, cancellationToken) is not { } document
                || ReadStats(matchId, document) is not { } stats)
            {
                continue;
            }

            await storageGuard.RunAsync($"saving the statistics per minute of base match {id}", async () =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var rows = await db.BaselineMetrics.Where(row => row.MatchId == id && row.StatHistory == null).ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    if (stats.HistoryOf(row.HeroId) is { } history)
                    {
                        row.StatHistory = StoredStatHistory.Serialize(history);
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
            });
            filled++;
        }

        if (filled > 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Filled the statistics per minute of {Matches} comparison base matches from their kept documents", filled);
            }
        }

        return filled;
    }

    private MatchStatTimeline? ReadStats(MatchId matchId, string document)
    {
        try
        {
            var response = JsonSerializer.Deserialize<ReplayParserMatchResponse>(document, DocumentOptions)
                ?? throw new InvalidDataException("Empty document.");
            return MatchStatTimeline.From(response.ToReplayAnalysis(matchId));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            logger.LogWarning(ex, "The kept document of base match {MatchId} can't be read", matchId.Value);
            return null;
        }
    }
}

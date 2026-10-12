using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.Replays;

// One row of the "replay_analyses" table: the result of parsing a match's replay ourselves (the Go parser).
// Only definitive answers get a row; a replay waiting or being parsed lives in the in-memory queue.
public class ReplayAnalysisRecord
{
    public long MatchId { get; set; }

    // "ready", "failed" or "expired" (see StoredReplayMappings).
    public string Status { get; set; } = "";

    // StoredReplayAnalysis as JSON; only for "ready".
    public string? Data { get; set; }

    // When the result was saved, from the app's clock (a failure is retried counting from it).
    public DateTimeOffset SavedAt { get; set; }
}

internal sealed class ReplayAnalysisRecordConfiguration : IEntityTypeConfiguration<ReplayAnalysisRecord>
{
    public void Configure(EntityTypeBuilder<ReplayAnalysisRecord> builder)
    {
        builder.HasKey(analysis => analysis.MatchId);
        // The ID comes from Dota, not from the database.
        builder.Property(analysis => analysis.MatchId).ValueGeneratedNever();
        builder.Property(analysis => analysis.Status).HasMaxLength(16);
        builder.Property(analysis => analysis.Data).HasColumnType("jsonb");
    }
}

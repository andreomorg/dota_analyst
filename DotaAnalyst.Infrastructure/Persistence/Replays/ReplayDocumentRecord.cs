using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.Replays;

// One row of the "replay_documents" table: the Go parser's whole POST /v1/match document of a match, exactly as it
// came (contract: replay-parser/internal/matchdata). Kept so later analyses (builds, map, fights) don't parse the
// replay again; the analyses in use today read replay_analyses.
public class ReplayDocumentRecord
{
    public long MatchId { get; set; }

    // The document's schemaVersion (a new version only adds fields; a breaking change is /v2/ in the parser).
    public int SchemaVersion { get; set; }

    public string Document { get; set; } = "";

    // From the app's clock.
    public DateTimeOffset SavedAt { get; set; }
}

internal sealed class ReplayDocumentRecordConfiguration : IEntityTypeConfiguration<ReplayDocumentRecord>
{
    public void Configure(EntityTypeBuilder<ReplayDocumentRecord> builder)
    {
        builder.HasKey(document => document.MatchId);
        // The ID comes from Dota, not from the database.
        builder.Property(document => document.MatchId).ValueGeneratedNever();
        // jsonb: PostgreSQL compresses it (TOAST) and later queries can read inside it.
        builder.Property(document => document.Document).HasColumnType("jsonb");
    }
}

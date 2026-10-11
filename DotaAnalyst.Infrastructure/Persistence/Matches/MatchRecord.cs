using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.Matches;

// One row of the "matches" table: a saved match. The whole aggregate goes into one JSON column, since
// it is always read and written whole; start_time and is_parsed are real columns so SQL can use them.
public class MatchRecord
{
    public long MatchId { get; set; }

    public DateTimeOffset StartTime { get; set; }

    // A parsed match is final: the save statement never overwrites it.
    public bool IsParsed { get; set; }

    // When the match was saved or last refreshed from OpenDota.
    public DateTimeOffset SavedAt { get; set; }

    // StoredMatchDetails as JSON (see StoredMatchMappings).
    public string Details { get; set; } = "";
}

internal sealed class MatchRecordConfiguration : IEntityTypeConfiguration<MatchRecord>
{
    public void Configure(EntityTypeBuilder<MatchRecord> builder)
    {
        builder.HasKey(match => match.MatchId);
        // The ID comes from Dota, not from the database.
        builder.Property(match => match.MatchId).ValueGeneratedNever();
        // jsonb: PostgreSQL stores it parsed (validated, compact, queryable) instead of as plain text.
        builder.Property(match => match.Details).HasColumnType("jsonb");
    }
}

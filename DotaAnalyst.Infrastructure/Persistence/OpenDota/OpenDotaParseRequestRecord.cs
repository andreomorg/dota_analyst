using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.OpenDota;

public sealed class OpenDotaParseRequestRecord
{
    public long MatchId { get; set; }
    public string State { get; set; } = "queued";
    public string? JobId { get; set; }
    public int SubmitAttempts { get; set; }
    public int PollAttempts { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public string? LastErrorCode { get; set; }
    public bool EnrichmentOnly { get; set; }
}

internal sealed class OpenDotaParseRequestConfiguration : IEntityTypeConfiguration<OpenDotaParseRequestRecord>
{
    public void Configure(EntityTypeBuilder<OpenDotaParseRequestRecord> builder)
    {
        builder.ToTable("opendota_parse_requests");
        builder.HasKey(row => row.MatchId);
        builder.Property(row => row.MatchId).ValueGeneratedNever();
        builder.Property(row => row.State).HasMaxLength(24);
        builder.Property(row => row.JobId).HasMaxLength(32);
        builder.Property(row => row.LastErrorCode).HasMaxLength(64);
        builder.HasIndex(row => new { row.State, row.NextAttemptAt });
    }
}

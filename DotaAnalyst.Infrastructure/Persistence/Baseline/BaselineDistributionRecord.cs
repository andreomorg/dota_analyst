using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.Baseline;

// One row of "baseline_distributions": the finished comparison base of one hero in one role from one source. Written
// by the collector's machine (its local database and the Neon mirror), read by the sites.
public class BaselineDistributionRecord
{
    public int HeroId { get; set; }

    public int Role { get; set; }

    // 0 is the general same-hero base; positive values are lane-opponent hero IDs.
    public int OpponentHeroId { get; set; }

    // "pro" or "highRank".
    public string Source { get; set; } = "";

    // How many matches the distributions came from.
    public int Matches { get; set; }

    // [{ "metric": "deathsPer30", "quantiles": [21 numbers], "sampleSize": 42 }, ...] as JSON.
    public string Distributions { get; set; } = "";

    public DateTimeOffset PublishedAt { get; set; }
}

internal sealed class BaselineDistributionRecordConfiguration : IEntityTypeConfiguration<BaselineDistributionRecord>
{
    public void Configure(EntityTypeBuilder<BaselineDistributionRecord> builder)
    {
        builder.HasKey(set => new { set.HeroId, set.Role, set.OpponentHeroId, set.Source });
        builder.Property(set => set.OpponentHeroId).HasDefaultValue(0);
        builder.Property(set => set.Source).HasMaxLength(16);
        builder.Property(set => set.Distributions).HasColumnType("jsonb");
    }
}

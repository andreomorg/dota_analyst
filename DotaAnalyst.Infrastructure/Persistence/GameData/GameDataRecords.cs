using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotaAnalyst.Infrastructure.Persistence.GameData;

// One row of "game_builds": a release of the game's files (GameBuild) and which file content each needed path has in
// it. Files point to game_files by their Git blob hash, so a file that did not change between builds is kept once.
public class GameBuildRecord
{
    // The d2vpkr commit the files were read from.
    public string CommitSha { get; set; } = "";

    // "Client 6962" in the commit message; null when it does not say.
    public int? ClientVersion { get; set; }

    public DateTimeOffset ReleasedAt { get; set; }

    // { "scripts/npc/items.txt": "<blob sha>", ... } as jsonb.
    public string Files { get; set; } = "{}";

    // From the app's clock.
    public DateTimeOffset SavedAt { get; set; }
}

// One row of "game_files": the text of one game file version, by its Git blob hash (the same content has the same
// hash in every build).
public class GameFileRecord
{
    public string BlobSha { get; set; } = "";

    public string Content { get; set; } = "";
}

internal sealed class GameBuildRecordConfiguration : IEntityTypeConfiguration<GameBuildRecord>
{
    public void Configure(EntityTypeBuilder<GameBuildRecord> builder)
    {
        builder.HasKey(build => build.CommitSha);
        builder.Property(build => build.Files).HasColumnType("jsonb");
        builder.HasIndex(build => build.ReleasedAt);
    }
}

internal sealed class GameFileRecordConfiguration : IEntityTypeConfiguration<GameFileRecord>
{
    public void Configure(EntityTypeBuilder<GameFileRecord> builder) => builder.HasKey(file => file.BlobSha);
}

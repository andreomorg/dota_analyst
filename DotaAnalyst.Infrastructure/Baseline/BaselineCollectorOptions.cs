namespace DotaAnalyst.Infrastructure.Baseline;

// "Collector" in the configuration (appsettings.json, or Collector__* variables in the compose).
public class BaselineCollectorOptions
{
    public const string SectionName = "Collector";

    // Off by default: only the collector container on the project owner's machine turns it on.
    public bool Enabled { get; set; }

    // At most this many matches collected per 24 h (each is one OpenDota call and one ~30 MB replay download).
    public int DailyLimit { get; set; } = 300;

    // Stop adding matches at this local database size, preserving everything already collected. Zero disables it.
    public long MaxDatabaseBytes { get; set; } = 40L * 1024 * 1024 * 1024;

    // Accounts whose recently played heroes the high-rank matches must have (the project owner's).
    public long[] FocusAccountIds { get; set; } = [];

    // High-rank matches the base should have of each of those heroes; until then their matches come first.
    public int TargetMatchesPerHero { get; set; } = 10;
}

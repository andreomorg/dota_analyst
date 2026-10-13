namespace DotaAnalyst.Infrastructure.Baseline;

// "Collector" in the configuration (appsettings.json, or Collector__* variables in the compose).
public class BaselineCollectorOptions
{
    public const string SectionName = "Collector";

    // Off by default: only the collector container on the project owner's machine turns it on.
    public bool Enabled { get; set; }

    // At most this many matches collected per 24 h (each is one OpenDota call and one ~30 MB replay download).
    public int DailyLimit { get; set; } = 300;

    // Accounts whose recently played heroes the high-rank matches must have (the project owner's).
    public long[] FocusAccountIds { get; set; } = [];
}

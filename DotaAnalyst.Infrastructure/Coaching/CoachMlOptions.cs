namespace DotaAnalyst.Infrastructure.Coaching;

public sealed class CoachMlOptions
{
    public const string SectionName = "CoachMl";
    public bool Enabled { get; set; }
    public Uri? BaseUrl { get; set; }
    public string ApiKey { get; set; } = "";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(190);
}

namespace DotaAnalyst.Infrastructure.OpenDota;

public class OpenDotaOptions
{
    public const string SectionName = "OpenDota";

    public required Uri BaseUrl { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}

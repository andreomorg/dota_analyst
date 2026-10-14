namespace DotaAnalyst.Infrastructure.OpenDota;

public sealed class OpenDotaParsingOptions
{
    public const string SectionName = "OpenDota:Parsing";
    public bool Enabled { get; set; }
    public int PollSeconds { get; set; } = 15;
    public int MaxPollSeconds { get; set; } = 60;
    public int ActiveMinutes { get; set; } = 10;
    public int ResumeMinutes { get; set; } = 15;
    public int MaxSubmitAttempts { get; set; } = 3;
    public int RetryHours { get; set; } = 24;
}

using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// When Valve's replay of a match can be trusted to be gone. Valve uploads the replay to its servers some time
// after the match ends (usually minutes, sometimes longer) and deletes it after a few weeks, so "Valve doesn't
// have the replay" right after the match only means "not uploaded yet". It only proves the replay expired when
// it was seen missing at least UploadWindow after the match ended.
public static class ReplayAvailability
{
    // Generous on purpose: a replay missing a day after the match is not coming anymore.
    public static readonly TimeSpan UploadWindow = TimeSpan.FromDays(1);

    // missingAt: when Valve answered that it doesn't have the replay.
    public static bool IsExpired(MatchDetails match, DateTimeOffset missingAt) =>
        missingAt - match.EndedAt >= UploadWindow;
}

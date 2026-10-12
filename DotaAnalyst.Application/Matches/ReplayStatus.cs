namespace DotaAnalyst.Application.Matches;

/// <summary>
/// Where our own reading of the match replay stands (the laning phase, for matches OpenDota didn't parse):
/// ready, processing (ask again in a few seconds), failed (tried again later), expired (Valve no longer has the
/// replay), unavailable (the feature is off or down right now) or notNeeded (OpenDota parsed the match).
/// </summary>
public enum ReplayStatus
{
    /// <summary>Parsed: <c>laning</c> is filled when the player is in the match.</summary>
    Ready,

    /// <summary>Waiting or being parsed in the background: ask again in a few seconds.</summary>
    Processing,

    /// <summary>The replay could not be read; it is tried again after a while.</summary>
    Failed,

    /// <summary>Valve no longer has the replay (they expire after a few weeks). Final.</summary>
    Expired,

    /// <summary>Replay parsing is off or temporarily down, or the replay's address is unknown. A later visit tries again.</summary>
    Unavailable,

    /// <summary>OpenDota parsed this match, so its data is already complete.</summary>
    NotNeeded,
}

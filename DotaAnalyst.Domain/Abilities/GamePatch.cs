namespace DotaAnalyst.Domain.Abilities;

// One official patch or sub-patch of the game (7.41, 7.41a ... 7.41f), with when Valve released it (the patch notes'
// own time). The game files of a build (GameBuild) come out a few hours after the update reaches players.
public sealed record GamePatch(string Name, DateTimeOffset ReleasedAt)
{
    // The patch without its letter: "7.41c" -> "7.41". Maps and balance belong to it; sub-patches change numbers.
    public string Major => Name.TrimEnd("abcdefghijklmnopqrstuvwxyz".ToCharArray());

    // The patch a match was played on: the last one released at or before the horn. None without a start time or
    // before the first patch known.
    public static GamePatch? For(IEnumerable<GamePatch> patches, DateTimeOffset? startedAt)
    {
        ArgumentNullException.ThrowIfNull(patches);
        return startedAt is { } start
            ? patches.Where(patch => patch.ReleasedAt <= start).MaxBy(patch => patch.ReleasedAt)
            : null;
    }

    // Whether a build's files are those of this patch: released at or after the patch and before the next one. A build
    // older than its match's patch means the files of the previous sub-patch were used (numbers may differ).
    public static bool BuildBelongsTo(GameBuild build, GamePatch patch, IEnumerable<GamePatch> patches)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(patches);
        var next = patches.Where(other => other.ReleasedAt > patch.ReleasedAt).MinBy(other => other.ReleasedAt);
        return build.ReleasedAt >= patch.ReleasedAt && (next is null || build.ReleasedAt < next.ReleasedAt);
    }
}

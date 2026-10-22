namespace DotaAnalyst.Domain.Abilities;

// One release of the game's files (a client update: Client 6962), identified by where its files were read from (the
// d2vpkr commit), with when it came out.
public sealed record GameBuild(string Id, int? ClientVersion, DateTimeOffset ReleasedAt)
{
    // The build a match was played on: the last one released at or before the horn. A match from before the first
    // build known (the start of the covered patch is not a build of its own) gets the first one when it is not older
    // than coveredSince; anything older, or a match without a start time, gets none (the current catalog is used).
    // The release time is when the files were published, 12 to 18 hours after the update reached players (checked
    // against Valve's patch notes for 7.41–7.41f). So with the match's official sub-patch, a match in that window gets
    // the first build of its sub-patch instead of the last one of the previous sub-patch; without it, the previous one.
    public static GameBuild? For(IEnumerable<GameBuild> builds, DateTimeOffset? startedAt, DateTimeOffset coveredSince,
        GamePatch? patch = null)
    {
        ArgumentNullException.ThrowIfNull(builds);
        if (startedAt is not { } start || start < coveredSince)
        {
            return null;
        }

        var ordered = builds.OrderBy(build => build.ReleasedAt).ToList();
        var build = ordered.LastOrDefault(build => build.ReleasedAt <= start) ?? ordered.FirstOrDefault();
        return patch is not null && build is not null && build.ReleasedAt < patch.ReleasedAt
            ? ordered.FirstOrDefault(candidate => candidate.ReleasedAt >= patch.ReleasedAt) ?? build
            : build;
    }
}

using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Domain.Analysis;

public sealed record ObservedFarmCandidate(string ResourceId, string Name, int X, int Y, int Health,
    int? BaseBountyMin, int? BaseBountyMax, double StraightLineDistance, double? NearestAllyDistance,
    bool? AllyCloser, bool? AccessEstablished = null, double? ClearSeconds = null);
public sealed record FarmCandidatesAt(bool Available, int? ObservedAt, string? UnavailableReason,
    IReadOnlyList<ObservedFarmCandidate> Candidates);

public static class FarmCandidates
{
    // Enumerates resources at an exact recorded instant. No stale units, invisible enemies or manually supplied price.
    public static FarmCandidatesAt At(int heroId, Team team, double seconds,
        IReadOnlyList<FarmResourceSnapshot>? snapshots, IReadOnlyList<HeroPositions> positions,
        IReadOnlyDictionary<int, Team> teams)
    {
        if (snapshots is null) return new(false, null, "absentResourceFamily", []);
        var snapshot = snapshots.SingleOrDefault(s => s.Seconds == seconds);
        if (snapshot is null) return new(false, null, "noExactResourceSnapshot", []);
        var self = positions.FirstOrDefault(p => p.HeroId == heroId)?.At(snapshot.Seconds, tolerance: 0);
        if (self is null) return new(false, snapshot.Seconds, "noExactLivingHeroPosition", []);
        var allies = positions.Where(p => p.HeroId != heroId && teams.TryGetValue(p.HeroId, out var allyTeam) && allyTeam == team)
            .Select(p => p.At(snapshot.Seconds, tolerance: 0)).Where(p => p is not null).Select(p => p!.Value).ToList();
        var ownRawTeam = team == Team.Radiant ? 2 : 3;
        double Distance(int x, int y, PositionSample p) => Math.Sqrt(Math.Pow((double)x - p.X, 2) + Math.Pow((double)y - p.Y, 2));
        var candidates = snapshot.Units.Where(u => u.RawTeam != ownRawTeam && u.SeenBy(team) == true)
            .Select(u =>
            {
                var distance = Distance(u.X, u.Y, self.Value);
                double? allyDistance = allies.Count == 0 ? null : allies.Min(p => Distance(u.X, u.Y, p));
                return new ObservedFarmCandidate(u.Id, u.Name, u.X, u.Y, u.Health, u.BaseBountyMin, u.BaseBountyMax,
                    distance, allyDistance, allyDistance is { } d ? d < distance : null);
            }).OrderBy(u => u.StraightLineDistance).ThenBy(u => u.ResourceId).ToList();
        return new(true, snapshot.Seconds, null, candidates);
    }
}

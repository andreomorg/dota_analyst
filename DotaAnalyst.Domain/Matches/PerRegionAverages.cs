namespace DotaAnalyst.Domain.Matches;

// The average of per-region values over several heroes (e.g. the match average of the time spent in each region).
public static class PerRegionAverages
{
    // Null when there is no hero or any hero lacks the regions (metrics computed before them).
    public static PerRegion<double>? OfAll(IEnumerable<PerRegion<double>?> heroes)
    {
        var all = heroes.ToList();
        return all.Count == 0 || all.Any(hero => hero is null)
            ? null
            : PerRegion<double>.Of(region => all.Average(hero => hero![region]));
    }

    // Only the heroes that have the regions; null when none has.
    public static PerRegion<double>? OfKnown(IEnumerable<PerRegion<double>?> heroes)
    {
        var known = heroes.OfType<PerRegion<double>>().ToList();
        return known.Count == 0 ? null : PerRegion<double>.Of(region => known.Average(hero => hero[region]));
    }

    // Each phase averaged on its own; null when there is no hero or any hero lacks the phases.
    public static PerGamePhase<PerRegion<double>>? ByPhaseOfAll(IEnumerable<PerGamePhase<PerRegion<double>>?> heroes)
    {
        var all = heroes.ToList();
        return all.Count == 0 || all.Any(hero => hero is null)
            ? null
            : new PerGamePhase<PerRegion<double>>(
                OfAll(all.Select(hero => hero!.EarlyGame))!,
                OfAll(all.Select(hero => hero!.MidGame))!,
                OfAll(all.Select(hero => hero!.LateGame))!);
    }
}

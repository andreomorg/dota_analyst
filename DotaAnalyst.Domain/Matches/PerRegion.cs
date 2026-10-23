namespace DotaAnalyst.Domain.Matches;

// Value object: one value per map region (time shares, death counts...). A record of fields, not a dictionary, so two
// with the same values are equal.
public record PerRegion<T>(
    T OwnBase, T EnemyBase, T TopLane, T MiddleLane, T BottomLane, T River, T OwnJungle, T EnemyJungle)
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "The factory reads naturally as PerRegion<int>.Of(...); used all over the analysis.")]
    public static PerRegion<T> Of(Func<MapRegion, T> valueOf) => new(
        valueOf(MapRegion.OwnBase), valueOf(MapRegion.EnemyBase), valueOf(MapRegion.TopLane),
        valueOf(MapRegion.MiddleLane), valueOf(MapRegion.BottomLane), valueOf(MapRegion.River),
        valueOf(MapRegion.OwnJungle), valueOf(MapRegion.EnemyJungle));

    public T this[MapRegion region] => region switch
    {
        MapRegion.OwnBase => OwnBase,
        MapRegion.EnemyBase => EnemyBase,
        MapRegion.TopLane => TopLane,
        MapRegion.MiddleLane => MiddleLane,
        MapRegion.BottomLane => BottomLane,
        MapRegion.River => River,
        MapRegion.OwnJungle => OwnJungle,
        _ => EnemyJungle,
    };

    public PerRegion<TResult> Select<TResult>(Func<T, TResult> map) => PerRegion<TResult>.Of(region => map(this[region]));
}

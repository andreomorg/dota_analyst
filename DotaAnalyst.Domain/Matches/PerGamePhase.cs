namespace DotaAnalyst.Domain.Matches;

// A value split by game-clock phase: early (0-10 min), mid (10-20 min), and late (20 min onward).
public record PerGamePhase<T>(T EarlyGame, T MidGame, T LateGame)
{
    public PerGamePhase<TResult> Select<TResult>(Func<T, TResult> map) =>
        new(map(EarlyGame), map(MidGame), map(LateGame));
}

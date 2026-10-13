namespace DotaAnalyst.Domain.Replays;

// One hero's damage with enemy heroes in each minute of the game clock (index i = minute i), read from the replay.
// Used in the parse to compute the metrics and never saved, like HeroPositions.
public sealed record HeroDamageMinutes
{
    public HeroDamageMinutes(int heroId, IReadOnlyList<int> toHeroes, IReadOnlyList<int> fromHeroes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentNullException.ThrowIfNull(toHeroes);
        ArgumentNullException.ThrowIfNull(fromHeroes);
        if (toHeroes.Any(value => value < 0) || fromHeroes.Any(value => value < 0))
        {
            throw new ArgumentException("Damage can't be negative.", nameof(toHeroes));
        }

        HeroId = heroId;
        ToHeroes = toHeroes.ToList();
        FromHeroes = fromHeroes.ToList();
    }

    public int HeroId { get; }

    // Dealt by the hero (with its illusions and summons) to enemy heroes.
    public IReadOnlyList<int> ToHeroes { get; }

    // Taken by the hero from enemy heroes (and their illusions and summons).
    public IReadOnlyList<int> FromHeroes { get; }
}

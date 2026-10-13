namespace DotaAnalyst.Domain.Replays;

// What dealt the killing blow of a hero death.
public enum KillerKind
{
    Hero,
    Tower,
    Creeps,
    Neutrals,
    Roshan,
    Other,
}

// Value object: one hero death read from the replay — when (game clock), where (map coordinates: roughly
// −8200..8200 on both axes, Radiant's base bottom-left) and what killed it.
public record HeroDeath
{
    public HeroDeath(
        int heroId, int seconds, int x, int y, KillerKind killer, int? killerHeroId, bool reincarnated,
        IReadOnlyList<int>? assistHeroIds = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        if (assistHeroIds is not null && assistHeroIds.Any(assist => assist <= 0 || assist == heroId))
        {
            throw new ArgumentException("Assists are other heroes.", nameof(assistHeroIds));
        }

        AssistHeroIds = assistHeroIds?.Distinct().Order().ToList() ?? [];
        if (killerHeroId is not null && killer != KillerKind.Hero)
        {
            throw new ArgumentException("Only a hero kill names the killer hero.", nameof(killerHeroId));
        }

        if (killerHeroId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(killerHeroId));
        }

        HeroId = heroId;
        Seconds = seconds;
        X = x;
        Y = y;
        Killer = killer;
        KillerHeroId = killerHeroId;
        Reincarnated = reincarnated;
    }

    // The hero that died.
    public int HeroId { get; }

    // Heroes the combat log credits with an assist (the killer left out), in hero ID order. A subset of the
    // scoreboard's assists, which credit more heroes.
    public IReadOnlyList<int> AssistHeroIds { get; }

    // A record compares lists by reference; deaths with the same assists are the same death.
    public virtual bool Equals(HeroDeath? other) =>
        other is not null && HeroId == other.HeroId && Seconds == other.Seconds && X == other.X && Y == other.Y
        && Killer == other.Killer && KillerHeroId == other.KillerHeroId && Reincarnated == other.Reincarnated
        && AssistHeroIds.SequenceEqual(other.AssistHeroIds);

    public override int GetHashCode() => HashCode.Combine(HeroId, Seconds, X, Y, Killer, KillerHeroId, Reincarnated);

    // Game clock of the death (negative before the horn).
    public int Seconds { get; }

    public int X { get; }

    public int Y { get; }

    public KillerKind Killer { get; }

    // The hero that got the kill; null for non-hero kills (and hero kills the replay does not attribute).
    public int? KillerHeroId { get; }

    // The hero came back (Aegis, Wraith King): not a death on the scoreboard.
    public bool Reincarnated { get; }

    // Died before the end of the laning phase.
    public bool InLaningPhase => Seconds < ReplayAnalysis.LaningPhaseEndsAtSeconds;
}

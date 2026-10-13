namespace DotaAnalyst.Domain.Replays;

public sealed record AbilityUpgrade
{
    public AbilityUpgrade(int heroId, int seconds, string abilityName, int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heroId);
        ArgumentException.ThrowIfNullOrWhiteSpace(abilityName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(level);

        HeroId = heroId;
        Seconds = seconds;
        AbilityName = abilityName;
        Level = level;
    }

    public int HeroId { get; }

    public int Seconds { get; }

    public string AbilityName { get; }

    public int Level { get; }
}

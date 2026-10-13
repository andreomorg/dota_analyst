namespace DotaAnalyst.Domain.Replays;

// Value object: damage dealt over the whole match from one source to one target with one ability/item (or a
// basic attack), read from the replay. A side that is a hero has its hero ID; the other side may be any unit
// (creeps, buildings, Roshan...), named by its kind. Illusions and summons count for their owner hero.
public record DamageRow
{
    // The kind of a player's hero itself (not its illusions or summons, which have the hero ID of their owner).
    public const string HeroKind = "hero";

    public DamageRow(int? sourceHeroId, string sourceKind, int? targetHeroId, string targetKind, string? inflictor, int amount)
    {
        if (sourceHeroId is null && targetHeroId is null)
        {
            throw new ArgumentException("A damage row has a hero on at least one side.", nameof(sourceHeroId));
        }

        if (sourceHeroId is <= 0 || targetHeroId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceHeroId), "Hero IDs are positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetKind);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        SourceHeroId = sourceHeroId;
        SourceKind = sourceKind;
        TargetHeroId = targetHeroId;
        TargetKind = targetKind;
        Inflictor = inflictor;
        Amount = amount;
    }

    public int? SourceHeroId { get; }

    // HeroKind for the hero itself, "illusion"/"summons" for its units, else the unit kind (laneCreeps, buildings,
    // roshan...).
    public string SourceKind { get; }

    // The damage was taken by a hero itself (damage to its illusions or summons isn't damage to the hero).
    public bool TargetIsHero => TargetHeroId is not null && TargetKind == HeroKind;

    public int? TargetHeroId { get; }

    public string TargetKind { get; }

    // Internal name of the ability or item (lich_frost_nova, item_dagon_5); null for a basic attack.
    public string? Inflictor { get; }

    public int Amount { get; }
}

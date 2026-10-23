using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// An inventory slot of a hero that changed at a second of the game clock (read every 2 s); Item null = it emptied.
// Slots: 0-5 the inventory, 6-8 the backpack (carried, not usable; only in documents since DATA-D16), 9-14 the stash,
// TeleportSlot, NeutralSlot (the neutral artifact) and EnhancementSlot (the neutral enhancement; only in documents with
// neutral crafts, N2).
public sealed record SlotChange(int HeroId, int Seconds, int Slot, string? Item)
{
    public const int TeleportSlot = 15;
    public const int NeutralSlot = 16;
    public const int EnhancementSlot = 17;
    public const int InventorySlots = 6;
    public const int FirstBackpackSlot = 6;
    public const int BackpackSlots = 3;
    public const int FirstStashSlot = 9;
    public const int StashSlots = 6;

    public bool IsBackpack => Slot is >= FirstBackpackSlot and < FirstBackpackSlot + BackpackSlots;
}

// A cooldown of a hero's item or ability that started at Seconds and ends at ReadyAt (the replay's own times).
public sealed record CooldownStart(int HeroId, double Seconds, string Name, double ReadyAt)
{
    // Drops of this cooldown while it ran (resets, reductions: DATA-D15), in time order. Each only counts from its own
    // moment on: before it, the cooldown still ran to ReadyAt as known then (never rewritten backwards).
    public IReadOnlyList<CooldownCorrection> Corrections { get; init; } = [];

    // Running at the second, by the ready time known then.
    public bool ActiveAt(int second) => Seconds <= second && ReadyAtAsOf(second) > second;

    // The ready time known at a second: the last correction made by then, else ReadyAt.
    public double ReadyAtAsOf(double second) => Corrections.LastOrDefault(change => change.Seconds <= second)?.ReadyAt ?? ReadyAt;

    // The length as it started (corrections do not change it).
    public double Length => ReadyAt - Seconds;
}

// A running cooldown of an item or ability that dropped (reset or reduction) at Seconds: ready at ReadyAt instead
// (items.cooldownChanges / abilities.cooldownChanges, DATA-D15). Entity and Serial identify the item/ability entity in
// the replay.
public sealed record CooldownCorrection(int HeroId, double Seconds, string Name, int Entity, int Serial, double ReadyAt);

// A neutral item a hero crafted with Madstones (7.38+): when, the artifact (item_ name) and its level, the tier (1-5),
// and the enhancement crafted with it and its level (null when the replay does not say).
public sealed record NeutralCraft(int HeroId, double Seconds, string Artifact, int? ArtifactLevel, int Tier, string? Enhancement,
    int? EnhancementLevel);

// A hero's Madstone counters, read every 2 s and kept only when one changes: in hand, acquired, found (neutral item
// tokens) and the gold from neutral creeps, all since the match started.
public sealed record MadstoneReading(int Seconds, int InHand, int Acquired, int Found, int NeutralGold);

public sealed record MadstoneTrack(int HeroId, IReadOnlyList<MadstoneReading> Readings)
{
    // The last reading at or before this second (the present, never the future); null before the first.
    public MadstoneReading? At(int seconds) => Readings.LastOrDefault(reading => reading.Seconds <= seconds);
}

// A hero's Madstones over the whole match (the last reading): acquired, found, still in hand at the end, and the gold
// from neutral creeps. Kept with the analysis (the readings themselves are not).
public sealed record NeutralProgress(int HeroId, int Acquired, int Found, int InHandAtEnd, int NeutralGold)
{
    public static NeutralProgress? From(MadstoneTrack track) =>
        track.Readings is [.., var last] ? new(track.HeroId, last.Acquired, last.Found, last.InHand, last.NeutralGold) : null;
}

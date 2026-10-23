using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// Transient factual observations from the archive. Missing and malformed families are never empty observations.
public sealed record ReplayAdditionalFacts
{
    public IReadOnlySet<string> InvalidFamilies { get; init; } = new HashSet<string>();
    public IReadOnlyList<OutpostObservation>? Outposts { get; init; }
    public IReadOnlyList<TormentorSite>? Tormentors { get; init; }
    public IReadOnlyList<CourierDeath>? CourierDeaths { get; init; }

    // combat.healing: the whole match's healing by source, target and inflictor (a final fact: never for a moment
    // before the end). Null when the document lacks it.
    public IReadOnlyList<HealingRow>? Healing { get; init; }

    // players[].facet is not read: in 7.41 there is no facet to choose (null for all 20 heroes of both local replays,
    // 22/10/2026); each hero's innate ability comes from the build's catalog (AbilityDetails.IsInnate).

    // players[].final and items.final: the scoreboard and the items at the end (final facts: never for a moment before
    // the end). Null when the document lacks them.
    public IReadOnlyList<FinalHeroStats>? FinalStats { get; init; }

    public IReadOnlyList<FinalInventory>? FinalItems { get; init; }

    // vision.chatWheel: every chat wheel phrase used, by its numeric ID only (never text). Null when the document lacks it.
    public IReadOnlyList<ChatWheelUse>? ChatWheel { get; init; }

    // The map's changing state read by MP3 (MapState.cs); each null when the document lacks it (documents before MP3).
    public IReadOnlyList<TempTreeChange>? TempTrees { get; init; }

    public IReadOnlyList<BossTrack>? BossTracks { get; init; }

    public IReadOnlyList<LanternUse>? Lanterns { get; init; }

    public IReadOnlyList<GoldEvent>? GoldEvents { get; init; }
    public IReadOnlyList<GoldBalance>? GoldBalances { get; init; }
    public IReadOnlyList<EconomyObservation>? EconomyStates { get; init; }
    public IReadOnlyList<GoldLossObservation>? GoldLossChanges { get; init; }
    public IReadOnlyList<FarmResourceSnapshot>? FarmSnapshots { get; init; }

    // abilities.stolen (DATA-D17): stolen abilities held and their cooldowns; null in documents before it.
    public IReadOnlyList<StolenAbility>? StolenAbilities { get; init; }

    public IReadOnlyList<StolenCooldown>? StolenCooldowns { get; init; }
}

// A stolen ability (Rubick's Spell Steal and the like) held by HeroId from From to To (null: until the end), with the
// level it had (the victim's), whether it was a hidden sub-ability, and its entity. Never the original hero's ability.
public sealed record StolenAbility(int HeroId, double From, double? To, string Name, int Entity, int Serial, int Level, bool Hidden)
{
    public bool HeldAt(double second) => From <= second && (To is not { } to || second < to);
}

// A cooldown that started on a stolen ability held by HeroId (ready at ReadyAt as known then), with its entity.
public sealed record StolenCooldown(int HeroId, double Seconds, string Name, int Entity, int Serial, double ReadyAt);

// A chat wheel phrase a hero's player used: Valve's phrase ID, nothing about what it says or means.
public sealed record ChatWheelUse(int HeroId, double Seconds, uint Phrase);

// A hero's numbers at the end of the match, from the replay itself (not OpenDota: the two may differ, neither replaces
// the other). Deaths as the scoreboard counts them (a death with Aegis does not count). StunSeconds: stuns applied to
// enemy heroes; TeamFightParticipation 0-1.
public sealed record FinalHeroStats(
    int HeroId, int Level, int Kills, int Deaths, int Assists, int LastHits, int Denies, int MissedLastHits, int NetWorth,
    int GoldEarned, int Xp, int HeroDamage, int TowerDamage, int Healing, double StunSeconds, double TeamFightParticipation,
    int CampsStacked, int ObserversPlaced, int SentriesPlaced, int WardsDestroyed, int GoldLostToDeath, int Gold);

// A hero's items at the end of the match, each slot kept apart: the 6 inventory slots (in order), the 3 backpack slots,
// teleport, neutral artifact and enhancement. Item names include item_; null is an empty slot.
public sealed record FinalInventory(
    int HeroId, IReadOnlyList<string?> Inventory, IReadOnlyList<string?> Backpack, string? Teleport, string? Neutral,
    string? Enhancement);

// Healing over the whole match from one source to one target with one ability/item (null: regeneration/unknown),
// like DamageRow: a side that is a player's unit has its hero ID, its kind says hero, illusion or summons; otherwise the
// unit kind. Hits: how many combat log entries were added up (null in documents without it).
public sealed record HealingRow(int? SourceHeroId, string SourceKind, int? TargetHeroId, string TargetKind, string? Inflictor,
    int Amount, int? Hits)
{
    // The hero healed itself: counted once, as healing done and as healing received.
    public bool SelfHeal => SourceHeroId is not null && SourceHeroId == TargetHeroId
        && SourceKind == DamageRow.HeroKind && TargetKind == DamageRow.HeroKind;
}

public sealed record OutpostObservation(double Seconds, int? EntityIndex, Team? Team, int? HeroId, string Source, string? RawTeam = null);
public sealed record AliveObservation(int Seconds, bool? Alive);
public sealed record TormentorSite(Team? Side, int X, int Y, IReadOnlyList<AliveObservation> Samples, string? RawSide = null);
public sealed record CourierDeath(double Seconds, int VictimHeroId, int? KillerHeroId);

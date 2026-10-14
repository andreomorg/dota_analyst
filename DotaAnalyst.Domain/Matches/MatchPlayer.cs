using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Domain.Matches;

// One of the ten players of a match.
public record MatchPlayer(
    // Null for players with an anonymous (private) profile.
    AccountId? AccountId,
    int HeroId,
    Team Team,
    Kda Kda,
    int LastHits,
    int Denies,
    int GoldPerMinute,
    int ExperiencePerMinute,
    int NetWorth,
    int HeroDamage,
    int TowerDamage,
    int HeroHealing,
    int Level,
    // Inventory items, without empty slots.
    IReadOnlyList<int> ItemIds,
    int? NeutralItemId,
    IReadOnlyList<Benchmark> Benchmarks,
    SupplementalPlayerStats? SupplementalStats = null);

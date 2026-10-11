using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Players;

namespace DotaAnalyst.Tests.Domain;

// Creates MatchPlayer instances for tests, so each test only spells out what it cares about.
internal static class MatchPlayerBuilder
{
    public static MatchPlayer Create(
        AccountId? accountId = null,
        int heroId = 1,
        Team team = Team.Radiant,
        IReadOnlyList<int>? itemIds = null,
        int? neutralItemId = null,
        IReadOnlyList<Benchmark>? benchmarks = null) =>
        new(
            AccountId: accountId,
            HeroId: heroId,
            Team: team,
            Kda: new Kda(1, 2, 3),
            LastHits: 100,
            Denies: 10,
            GoldPerMinute: 400,
            ExperiencePerMinute: 500,
            NetWorth: 12000,
            HeroDamage: 15000,
            TowerDamage: 1000,
            HeroHealing: 0,
            Level: 20,
            ItemIds: itemIds ?? [],
            NeutralItemId: neutralItemId,
            Benchmarks: benchmarks ?? []);

    public static MatchPlayer WithBenchmarks(params Benchmark[] benchmarks) => Create(benchmarks: benchmarks);

    public static AccountId Account(long id)
    {
        AccountId.TryCreate(id, out var accountId);
        return accountId;
    }
}

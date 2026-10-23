using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Application.Matches;

/// <summary>The player's damage over the whole match (illusions and summons count for their owner).</summary>
/// <param name="ToHeroes">Damage dealt to each enemy hero, highest first.</param>
/// <param name="FromHeroes">Damage taken from each enemy hero, highest first.</param>
/// <param name="ByInflictor">Damage dealt to enemy heroes by each ability or item, highest first; inflictor null = basic attacks.</param>
public record PlayerDamage(
    IReadOnlyList<HeroAmount> ToHeroes,
    IReadOnlyList<HeroAmount> FromHeroes,
    IReadOnlyList<InflictorAmount> ByInflictor);

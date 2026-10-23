using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

/// <summary>What happened in the window: the hero's combat, signals and casts, the map's state around it, and the
/// match's objective events (outposts captured, couriers killed) and the hero's stolen abilities' cooldowns.</summary>
public sealed record ReplayFactsEvents(
    CombatWindowFacts Combat, CommunicationWindowFacts Communication, MapWindowFacts Map,
    IReadOnlyList<OutpostObservation>? Outposts, IReadOnlyList<CourierDeath>? CourierDeaths,
    IReadOnlyList<StolenCooldown>? StolenCooldowns);

using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

/// <summary>The hero at the anchor: its state (each source apart), its items, and the stolen abilities it held then.</summary>
public sealed record ReplayFactsSnapshot(HeroStateFacts Hero, InventoryAt Items, IReadOnlyList<StolenAbility>? StolenHeld,
    IReadOnlyList<TormentorAtAnchor>? Tormentors);

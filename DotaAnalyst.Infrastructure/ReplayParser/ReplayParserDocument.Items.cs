using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// uses: every item activated, same shape as the purchases (read only for the prediction models' samples).
internal sealed record ReplayParserItems(
    IReadOnlyList<ReplayParserPlayerItems?>? Purchases, IReadOnlyList<ReplayParserPlayerItems?>? Uses = null,
    IReadOnlyList<ReplayParserPlayerSlots?>? Slots = null, IReadOnlyList<ReplayParserPlayerCooldowns?>? Cooldowns = null,
    IReadOnlyList<ReplayParserPlayerCrafts?>? NeutralCrafts = null, JsonElement? Final = null,
    IReadOnlyList<ReplayParserPlayerCooldownChanges?>? CooldownChanges = null);

// One player's purchases as [t, "item_name"] tuples (JSON arrays, read one by one in the mapping).
internal sealed record ReplayParserPlayerItems(int? Player, IReadOnlyList<JsonElement>? Items);

// uses: one entry per player with every cast as [t, "ability_name"] (read only for the prediction models' samples).
internal sealed record ReplayParserAbilities(
    IReadOnlyList<ReplayParserPlayerAbilityLevels?>? Upgrades, IReadOnlyList<ReplayParserPlayerAbilities?>? Uses = null,
    IReadOnlyList<ReplayParserPlayerCooldowns?>? Cooldowns = null,
    IReadOnlyList<ReplayParserPlayerCooldownChanges?>? CooldownChanges = null, JsonElement? Stolen = null);

// One player's cooldowns that started, as [t, "name", readyAt] (newer documents).
internal sealed record ReplayParserPlayerCooldowns(int? Player, IReadOnlyList<JsonElement>? Cooldowns);

// One player's cooldown drops (DATA-D15), as [t, "name", entity, serial, readyAt].
internal sealed record ReplayParserPlayerCooldownChanges(int? Player, IReadOnlyList<JsonElement>? Changes);

// One player's inventory changes, as [t, slot, "item_name" | null] (newer documents).
internal sealed record ReplayParserPlayerSlots(int? Player, IReadOnlyList<JsonElement>? Changes);

// Targets (X27): parallel to Abilities, [targetPlayer | null, x | null, y | null, pointSource | null].
internal sealed record ReplayParserPlayerAbilities(int? Player, IReadOnlyList<JsonElement>? Abilities, IReadOnlyList<JsonElement>? Targets = null);

internal sealed record ReplayParserPlayerAbilityLevels(int? Player, IReadOnlyList<JsonElement>? Levels);

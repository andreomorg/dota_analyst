using System.Text.Json;
using DotaAnalyst.Infrastructure.Coaching;
using DotaAnalyst.Infrastructure.ReplayParser;

namespace DotaAnalyst.Tests.Infrastructure;

public class CooldownMappingTests
{
    [Fact]
    public void ZeroLengthCooldown_IsLeftOut_NotAnUnreadableDocument()
    {
        // Seen in kept documents: a pre-game TP scroll "ready" the moment its cooldown starts.
        var response = JsonSerializer.Deserialize<ReplayParserMatchResponse>("""
            { "players":[{"player":0,"heroId":1,"team":"radiant"}],
              "vision":{"smokes":[]},
              "items":{"uses":[],"cooldowns":[{"player":0,"cooldowns":[[-69.8,"item_tpscroll",-69.8],[120,"item_tpscroll",220]]}]},
              "abilities":{"uses":[],"cooldowns":[{"player":0,"cooldowns":[[300,"antimage_blink",300]]}]} }
            """, CoachingJson.Options)!;

        var activity = ReplayParserMappings.ReadActivity(response)!;

        var kept = Assert.Single(activity.ItemCooldowns!);
        Assert.Equal((120, 220), (kept.Seconds, kept.ReadyAt));
        Assert.Empty(activity.AbilityCooldowns!);
    }
}

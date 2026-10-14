using System.Text.Json;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Infrastructure.OpenDota;
using DotaAnalyst.Infrastructure.Persistence.Matches;

namespace DotaAnalyst.Tests.Infrastructure;

public sealed class OpenDotaEnrichmentTests
{
    [Fact]
    public void MissingEmptyAndZero_AreDistinct_AndStoredRoundtripPreservesThem()
    {
        const string json = """
            {"match_id":123,"version":22,"radiant_win":true,"players":[
              {"hero_id":1,"player_slot":0,"ability_uses":{"ability":0},"item_uses":{},"damage_inflictor_received":null,
               "actions_per_min":0,"actions":{"1":2},"gold_reasons":{"1":-50},"xp_reasons":{"1":200}}
            ]}
            """;
        var response = JsonSerializer.Deserialize<OpenDotaMatch>(json, OpenDotaClient.JsonOptions)!;
        var saved = StoredMatchMappings.FromJson(StoredMatchMappings.ToJson(response.ToMatchDetails()));
        Assert.Equal(SupplementalStatsSchema.CurrentVersion, saved.SupplementalStatsVersion);
        var extras = saved.Players[0].SupplementalStats!;
        Assert.Null(extras.DamageReceived);
        Assert.Empty(extras.ItemUses!);
        Assert.Equal(0, extras.AbilityUses!["ability"]);
        Assert.Equal(0, extras.ActionsPerMinute);
        Assert.Equal(2, extras.Actions!["1"]);
        Assert.Equal(-50, extras.GoldSources!["1"]);
        Assert.Equal(200, extras.ExperienceSources!["1"]);
    }

    [Fact]
    public void LegacyStoredMatch_HasNoExtras_AndRequiresEnrichment()
    {
        const string json = """
            {"version":1,"matchId":123,"durationSeconds":0,"startTime":"2026-10-01T00:00:00Z",
             "winningTeam":"radiant","radiantScore":0,"direScore":0,"isParsed":true,"players":[]}
            """;
        var legacy = StoredMatchMappings.FromJson(json);
        Assert.True(legacy.IsParsed);
        Assert.Equal(0, legacy.SupplementalStatsVersion);
    }
}

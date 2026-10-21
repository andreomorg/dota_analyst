using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;

namespace DotaAnalyst.Tests.Server;

internal sealed class KitApiCatalog : IAbilityCatalog
{
    public bool Unavailable { get; set; }
    public Task<IReadOnlyDictionary<string, AbilityDetails>> GetAbilitiesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, AbilityDetails>>(Unavailable ? new Dictionary<string, AbilityDetails>() : new()
        {
            ["lich_frost_nova"] = new("lich_frost_nova", "Frost Blast", "Blasts the target enemy unit.", ["Unit Target"],
                AbilityTargetTeam.Enemy, ["Hero", "Basic"], AbilityDamageType.Magical, false, Dispellable.Yes,
                [7], [110, 130, 150, 170], [new("damage", "DAMAGE:", [50, 100, 150, 200])]),
        });
    public Task<IReadOnlyDictionary<int, HeroKit>> GetKitsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<int, HeroKit>>(Unavailable ? new Dictionary<int, HeroKit>() : new()
        {
            [31] = new(31, ["lich_frost_nova"], [new("special_bonus_hp_regen_4", 1)],
                [new(0, "lich_growing_cold", "Growing Cold", "Frost Blast effects linger.", true)]),
        });
    public Task<string?> GetPatchAsync(CancellationToken cancellationToken) => Task.FromResult(Unavailable ? null : "7.41");
}

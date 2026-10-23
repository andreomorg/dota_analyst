using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Replays;

// A recorded activation (item or ability) with the replay's own time, not rounded: rounding 100.4 to 100 would let a use
// after a second leak into it. It says something was used, not that it was ready, hit, or worked.
// TargetHeroId/TargetX/TargetY (abilities only, X27): the hero it was cast on and the point it was cast at, when the
// replay links them (null: unknown, not "no target").
public sealed record RecordedUse(int HeroId, double Seconds, string Name, int? TargetHeroId = null, int? TargetX = null, int? TargetY = null,
    int? PointSource = null)
{
    public string PointOrigin => PointSource switch { 0 => "combatLogLocation", 1 => "castPositionOrder", _ => "unknown" };
}

// A Smoke of Deceit used, and the team of the hero who used it.
public sealed record SmokeUse(int HeroId, Team Team, double Seconds);

// Something a team did at a moment: a scan, a glyph.
public sealed record TeamAction(Team Team, double Seconds);

// A buyback of a hero.
public sealed record BuybackUse(int HeroId, double Seconds, int? Cost = null);

// A rune a hero picked up; RuneType is Valve's DOTA_RUNES number (replay-parser collect_vision.go).
public sealed record RunePickup(int HeroId, Team Team, double Seconds, int RuneType)
{
    // Double damage, haste, illusion, invisibility, regeneration, arcane, shield: the ones that change a fight.
    private static readonly HashSet<int> PowerRunes = [0, 1, 2, 3, 4, 6, 9];

    public bool IsPower => IsPowerType(RuneType);

    public static bool IsPowerType(int runeType) => PowerRunes.Contains(runeType);
}

// A ping on the map by a player (raw type kept by the parser), at a point.
public sealed record MapPing(int HeroId, Team Team, double Seconds, int X, int Y, int Type);

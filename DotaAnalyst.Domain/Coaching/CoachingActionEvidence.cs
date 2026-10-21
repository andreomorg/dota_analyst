using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Domain.Coaching;

// Technical replay identity only: no player name, account, final statistics or inferred role.
public sealed record CoachingHeroIdentity(int HeroId, Team Team, string HeroName);

// A recorded activation, not evidence of inventory or readiness at a later instant.
public sealed record CoachingRecordedUse(int HeroId, double Seconds, string Name);

public sealed record CoachingActionEvidence(
    int SchemaVersion, int HeroId, int AnchorSeconds, int WindowStartSeconds,
    IReadOnlyList<CoachingHeroIdentity> Heroes,
    IReadOnlyList<CoachingRecordedUse>? ItemUses,
    IReadOnlyList<CoachingRecordedUse>? AbilityUses);

public static class CoachingActionEvidenceBuilder
{
    public const int InspectionSeconds = 30;

    public static CoachingActionEvidence Build(int heroId, int anchorSeconds,
        IReadOnlyList<CoachingHeroIdentity> heroes,
        IReadOnlyList<CoachingRecordedUse>? itemUses,
        IReadOnlyList<CoachingRecordedUse>? abilityUses)
    {
        ArgumentNullException.ThrowIfNull(heroes);
        ArgumentOutOfRangeException.ThrowIfNegative(anchorSeconds);
        const string heroPrefix = "npc_dota_hero_";
        if (heroes.Count is < 1 or > 10 || heroes.Any(hero => hero is null || hero.HeroId <= 0
                || !Enum.IsDefined(hero.Team) || !IsKey(hero.HeroName)
                || !hero.HeroName.StartsWith(heroPrefix, StringComparison.Ordinal)
                || hero.HeroName.Length <= heroPrefix.Length)
            || heroes.Select(hero => hero.HeroId).Distinct().Count() != heroes.Count
            || !heroes.Any(hero => hero.HeroId == heroId))
            throw new ArgumentException("Expected unique technical hero identities including the viewer.", nameof(heroes));

        var knownHeroes = heroes.Select(hero => hero.HeroId).ToHashSet();
        var lower = anchorSeconds - InspectionSeconds;

        IReadOnlyList<CoachingRecordedUse>? Window(IReadOnlyList<CoachingRecordedUse>? uses)
        {
            if (uses is null) return null; // Missing source is distinct from no recorded uses.
            if (uses.Any(use => use is null || !knownHeroes.Contains(use.HeroId)
                    || !double.IsFinite(use.Seconds) || !IsKey(use.Name)))
                throw new ArgumentException("Invalid recorded use or actor outside the replay roster.", nameof(uses));
            // Compare precise source times before selecting. Rounding 100.1 to 100 would leak future information.
            // Equal timestamps retain source order; they do not establish causal order.
            return uses.Where(use => use.HeroId == heroId && use.Seconds >= lower && use.Seconds <= anchorSeconds)
                .OrderBy(use => use.Seconds).ToArray();
        }

        return new(1, heroId, anchorSeconds, lower, heroes.OrderBy(hero => hero.HeroId).ToArray(),
            Window(itemUses), Window(abilityUses));
    }

    private static bool IsKey(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '_');
}

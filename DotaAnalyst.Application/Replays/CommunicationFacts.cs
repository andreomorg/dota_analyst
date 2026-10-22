using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

/// <summary>What a hero's player signalled and cast in a closed window [From, To] of the game clock: map pings by Valve's
/// raw ping type, chat wheel phrases by ID (never text, never a reading of intent), and ability casts with what is known
/// of their target — a hero, or a point, by where the point came from: the combat log's location of the effect, or the
/// player's cast order (where the player clicked, not where the effect landed), or unknown. A cast without a target is
/// still a cast. Null counts: the family is unavailable (see the availabilities).</summary>
public sealed record CommunicationWindowFacts(
    FactAvailability Pings, FactAvailability ChatWheel, FactAvailability Casts, int From, double To,
    IReadOnlyDictionary<int, int>? PingsByType, IReadOnlyDictionary<uint, int>? PhrasesById,
    int? CastCount, int? CastsAtHero, int? CastPointsFromCombatLog, int? CastPointsFromOrders, int? CastPointsOfUnknownOrigin);

// Read-only facts about signals and casts for the internal factual consumer (D18). Only what happened inside the window.
public static class CommunicationFacts
{
    public static CommunicationWindowFacts Window(ReplayActivity? activity, ReplayAdditionalFacts? facts, int heroId, int from, double to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfLessThan(to, from);
        bool Inside(double seconds) => seconds >= from && seconds <= to;

        var pings = activity?.Pings?.Where(ping => ping.HeroId == heroId && Inside(ping.Seconds))
            .GroupBy(ping => ping.Type).ToDictionary(group => group.Key, group => group.Count());
        var phrases = facts?.ChatWheel?.Where(use => use.HeroId == heroId && Inside(use.Seconds))
            .GroupBy(use => use.Phrase).ToDictionary(group => group.Key, group => group.Count());
        var casts = activity?.AbilityUses.Where(use => use.HeroId == heroId && Inside(use.Seconds)).ToList();
        int? Points(string origin) => casts?.Count(use => use.TargetX is not null && use.PointOrigin == origin);

        return new CommunicationWindowFacts(
            pings is null ? FactAvailability.Unavailable : FactAvailability.Available,
            phrases is null ? FactAvailability.Unavailable : FactAvailability.Available,
            casts is null ? FactAvailability.Unavailable : FactAvailability.Available,
            from, to, pings, phrases,
            casts?.Count, casts?.Count(use => use.TargetHeroId is not null),
            Points("combatLogLocation"), Points("castPositionOrder"), Points("unknown"));
    }
}

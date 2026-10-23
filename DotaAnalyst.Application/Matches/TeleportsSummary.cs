namespace DotaAnalyst.Application.Matches;

/// <summary>The hero's teleports (TP scroll, Boots of Travel), from 0:00.</summary>
/// <param name="Teleports">Teleports completed; null when one can't be told.</param>
/// <param name="Cancelled">Teleport channels that ended without a teleport; null when one can't be told.</param>
/// <param name="IntoFights">Completed teleports that arrived into a team fight; null when one can't be told.</param>
public record TeleportsSummary(int? Teleports, int? Cancelled, int? IntoFights);

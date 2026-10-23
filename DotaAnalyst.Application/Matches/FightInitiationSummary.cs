namespace DotaAnalyst.Application.Matches;

/// <summary>Who started the team fights the hero was in (the first hero-on-hero hit between the teams).</summary>
/// <param name="Fights">Team fights the hero was in.</param>
/// <param name="TeamStarted">Fights whose first hits all came from the hero's team; null without the hits in the replay.</param>
/// <param name="HeroStarted">Of those, the ones the hero was among the first attackers; null without the hits in the replay.</param>
public record FightInitiationSummary(int Fights, int? TeamStarted, int? HeroStarted);

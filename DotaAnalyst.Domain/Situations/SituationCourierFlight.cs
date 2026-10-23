namespace DotaAnalyst.Domain.Situations;

// Any courier of the team is both alive and flying at the anchor. Missing readings remain unknown.
public sealed record SituationCourierFlight(bool? OwnAliveFlying = null, bool? HiddenEnemyAliveFlying = null);

using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Analysis;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Situations;

namespace DotaAnalyst.Application.Models;

// Use case: how the position estimator did on one kept replay, for measuring it (work/agents/plan.md R3). For each hero:
// the role queue's position (null without one), the old "richest at 10:00" rule (RoleInference) and the estimator's
// answer at fixed seconds. Reads only the kept document: no parse, no training.
public sealed class GetPositionEstimatesHandler(IArchivedReplaySource replays, IHeroCatalog heroCatalog)
{
    public static readonly int[] Checkpoints = [60, 180, 300, 600, 1200];

    public async Task<PositionEstimatesResult> HandleAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        if (await replays.FindAsync(matchId, cancellationToken) is not { } replay)
        {
            return new(ModelSamplesStatus.NoDocument, null);
        }

        var heroes = (await heroCatalog.GetHeroesAsync(cancellationToken)).Values
            .Where(hero => hero.Details is not null)
            .ToDictionary(hero => hero.Id, hero => hero.Details!);
        if (HeroSituationBuilder.From(replay.Analysis, replay.Positions, replay.Activity, new Domain.Abilities.GameCatalog(heroes)) is not { } builder)
        {
            return new(ModelSamplesStatus.Outdated, null);
        }

        var metrics = (replay.Analysis.Metrics ?? []).ToDictionary(hero => hero.HeroId);
        var oldRule = RoleInference.Infer(replay.Analysis.Players);
        var checkpoints = Checkpoints.Where(second => second <= builder.MatchEndsAtSeconds).ToList();
        var estimates = checkpoints.ToDictionary(second => second, second =>
            new[] { Team.Radiant, Team.Dire }.SelectMany(team => builder.EstimatedPositions(team, second))
                .ToDictionary(pair => pair.Key, pair => pair.Value));

        var rows = replay.Analysis.Players.Select(player => new PositionEstimateRow(
                player.HeroId,
                player.Team,
                metrics.GetValueOrDefault(player.HeroId) is { Role: { } role, RoleInferred: false } ? role : null,
                oldRule.TryGetValue(player.HeroId, out var old) ? old : null,
                checkpoints.Where(second => estimates[second].ContainsKey(player.HeroId))
                    .Select(second => new PositionEstimateAt(second, estimates[second][player.HeroId].Position,
                        estimates[second][player.HeroId].Confidence))
                    .ToList()))
            .ToList();
        return new(ModelSamplesStatus.Found, new PositionEstimateSet(matchId.ToString(), rows));
    }
}

public sealed record PositionEstimatesResult(ModelSamplesStatus Status, PositionEstimateSet? Estimates);

/// <summary>The position estimates of one match's heroes (internal: for measuring the estimator).</summary>
public sealed record PositionEstimateSet(string MatchId, IReadOnlyList<PositionEstimateRow> Heroes);

/// <summary>One hero: the role queue's position (null without one), the old 10:00 rule and the estimates.</summary>
public sealed record PositionEstimateRow(int HeroId, Team Team, int? RoleQueue, int? OldRule, IReadOnlyList<PositionEstimateAt> Estimates);

/// <summary>The estimator's position and confidence at a second.</summary>
public sealed record PositionEstimateAt(int Second, int Position, double Confidence);

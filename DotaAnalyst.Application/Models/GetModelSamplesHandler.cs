using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Abilities;
using DotaAnalyst.Domain.DeathRisk;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Situations;

namespace DotaAnalyst.Application.Models;

public enum ModelSamplesStatus
{
    Found,

    // No kept document for the match (or no storage on this machine).
    NoDocument,

    // The kept document lacks what a situation needs (HeroSituationBuilder.From).
    Outdated,
}

public sealed record ModelSamplesResult<T>(ModelSamplesStatus Status, ModelSampleSet<T>? Samples);

// Use case: the examples of one kept replay for a prediction model, for the Python trainer (coach-ml risk-export).
// Every model shares the situation columns (SituationRow); each adds its own label.
// The current patch's game data (GameCatalogLoader: hero profiles, items, abilities, kits) feeds the draft, the item
// spikes and the kits; an unavailable catalog only leaves them unknown.
public sealed class GetModelSamplesHandler(IArchivedReplaySource replays, GameCatalogLoader catalogs)
{
    public Task<ModelSamplesResult<DeathRiskSampleRow>> DeathRiskAsync(MatchId matchId, CancellationToken cancellationToken) =>
        HandleAsync(matchId, DeathRiskSampleBuilder.SampleEverySeconds, DeathRiskSampleBuilder.HorizonSeconds,
            (replay, catalog) => DeathRiskSampleBuilder.Build(replay.Analysis, replay.Positions, replay.Activity, catalog)?
                .Select(sample => new DeathRiskSampleRow(
                    SituationRow.From(sample.Situation, replay.Activity is not null), sample.DiesSoon, sample.DiedAtSeconds))
                .ToList(),
            cancellationToken);

    public Task<ModelSamplesResult<NextRegionSampleRow>> NextRegionAsync(MatchId matchId, CancellationToken cancellationToken) =>
        HandleAsync(matchId, NextRegionSampleBuilder.SampleEverySeconds, NextRegionSampleBuilder.HorizonSeconds,
            (replay, catalog) => NextRegionSampleBuilder.Build(replay.Analysis, replay.Positions, replay.Activity, catalog)?
                .Select(sample => new NextRegionSampleRow(
                    SituationRow.From(sample.Situation, replay.Activity is not null), sample.RegionLater))
                .ToList(),
            cancellationToken);

    private async Task<ModelSamplesResult<T>> HandleAsync<T>(
        MatchId matchId, int sampleEverySeconds, int horizonSeconds,
        Func<ArchivedReplay, GameCatalog, IReadOnlyList<T>?> rows,
        CancellationToken cancellationToken)
    {
        if (await replays.FindAsync(matchId, cancellationToken) is not { } replay)
        {
            return new(ModelSamplesStatus.NoDocument, null);
        }

        return rows(replay, await catalogs.LoadAsync(cancellationToken)) is { } samples
            ? new(ModelSamplesStatus.Found, new ModelSampleSet<T>(
                SituationRow.FeatureVersion, matchId.ToString(), sampleEverySeconds, horizonSeconds, samples))
            : new(ModelSamplesStatus.Outdated, null);
    }
}

/// <summary>The examples of one match for a prediction model (internal: for the local trainer).</summary>
/// <param name="FeatureVersion">Version of the situation columns; the trainer refuses mixed versions.</param>
/// <param name="MatchId">Match ID as text.</param>
/// <param name="SampleEverySeconds">Game-clock seconds between two examples of the same hero.</param>
/// <param name="HorizonSeconds">How far ahead the label looks.</param>
/// <param name="Samples">One row per living hero per sampled second.</param>
public sealed record ModelSampleSet<T>(
    int FeatureVersion, string MatchId, int SampleEverySeconds, int HorizonSeconds, IReadOnlyList<T> Samples);

/// <summary>A death-risk example: the situation and whether the hero died in the next HorizonSeconds.</summary>
public sealed record DeathRiskSampleRow(SituationRow Situation, bool DiesSoon, int? DiedAtSeconds);

/// <summary>A next-region example: the situation and the map region HorizonSeconds later (null = dead then).</summary>
public sealed record NextRegionSampleRow(SituationRow Situation, MapRegion? RegionLater);

using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Heroes;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;

namespace DotaAnalyst.Application.Replays;

public sealed record TormentorAtAnchor(DotaAnalyst.Domain.Matches.Team? Side, int X, int Y,
    AliveObservation? Observation, string? RawSide);

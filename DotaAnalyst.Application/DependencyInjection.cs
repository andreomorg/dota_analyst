using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Application.Baseline;
using DotaAnalyst.Application.Coaching;
using DotaAnalyst.Application.Matches;
using DotaAnalyst.Application.Models;
using DotaAnalyst.Application.Players;
using DotaAnalyst.Application.Replays;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotaAnalyst.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // The clock as a service, so tests can replace it ("Try": keeps one that was already registered).
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<DotaAnalyst.Application.Catalogs.GetHeroKitHandler>();
        services.AddScoped<DotaAnalyst.Application.Catalogs.GetHeroProfileHandler>();
        services.AddScoped<DotaAnalyst.Application.Heroes.GetHeroStateHandler>();
        services.AddScoped<DotaAnalyst.Application.Catalogs.GetItemHandler>();
        services.AddScoped<GetRecentMatchesHandler>();
        services.AddScoped<ReviewCoachingHandler>();
        services.AddScoped<GameCatalogLoader>();
        services.AddScoped<GetModelSamplesHandler>();
        services.AddScoped<GetPositionEstimatesHandler>();
        services.AddScoped<MeasureVisionHandler>();
        services.AddScoped<GetReplayFactsHandler>();
        services.AddScoped<GetMatchAnalysisHandler>();
        services.AddScoped<GetUserProfileHandler>();
        // Used by the background worker that empties the replay parse queue (Infrastructure), one scope per replay.
        services.AddScoped<ParseReplayHandler>();
        // The local re-parse for the prediction models (internal route).
        services.AddScoped<RefreshReplayHandler>();
        // Used by the comparison base collector (Infrastructure, only when Collector:Enabled), one scope per step.
        services.AddScoped<CollectBaselineHandler>();
        // Turns the raw base into the finished one and publishes it (collector's machine only).
        services.AddScoped<PublishBaselineHandler>();
        // Compares with the finished base in the site's database (local, or the Neon mirror in production).
        services.AddScoped<IBaselineProvider, DistributionBaselineProvider>();
        return services;
    }
}

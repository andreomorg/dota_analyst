using DotaAnalyst.Application.Matches;
using DotaAnalyst.Application.Players;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotaAnalyst.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // The clock as a service, so tests can replace it ("Try": keeps one that was already registered).
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<GetRecentMatchesHandler>();
        services.AddScoped<GetMatchAnalysisHandler>();
        services.AddScoped<GetUserProfileHandler>();
        return services;
    }
}

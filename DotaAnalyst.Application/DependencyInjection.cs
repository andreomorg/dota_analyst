using DotaAnalyst.Application.Matches;
using Microsoft.Extensions.DependencyInjection;

namespace DotaAnalyst.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GetRecentMatchesHandler>();
        services.AddScoped<GetMatchAnalysisHandler>();
        return services;
    }
}

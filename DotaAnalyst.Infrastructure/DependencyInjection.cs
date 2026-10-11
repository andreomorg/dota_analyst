using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.OpenDota;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        services.AddOptions<OpenDotaOptions>()
            .Bind(configuration.GetSection(OpenDotaOptions.SectionName))
            .Validate(
                options => options.BaseUrl is { IsAbsoluteUri: true } && options.BaseUrl.AbsolutePath.EndsWith('/'),
                "OpenDota:BaseUrl must be an absolute URL ending with '/'.")
            .Validate(options => options.Timeout > TimeSpan.Zero, "OpenDota:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<OpenDotaClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<OpenDotaOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.Timeout = options.Timeout;
        });

        services.AddScoped<IMatchProvider, OpenDotaMatchProvider>();
        services.AddScoped<IHeroCatalog, OpenDotaHeroCatalog>();
        services.AddScoped<IReplayAnalyzer, OpenDotaReplayAnalyzer>();
        services.AddScoped<IItemCatalog, OpenDotaItemCatalog>();

        return services;
    }
}

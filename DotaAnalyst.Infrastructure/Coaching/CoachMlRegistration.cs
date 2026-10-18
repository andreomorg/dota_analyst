using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Infrastructure.ReplayParser;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.Coaching;

public static class CoachMlRegistration
{
    public static IServiceCollection AddCoachMl(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(CoachMlOptions.SectionName);
        services.TryAddSingleton<IReplayDocumentArchive, NoReplayDocumentArchive>();
        services.AddOptions<CoachMlOptions>().Bind(section).Validate(o => !o.Enabled ||
            (o.BaseUrl is { IsAbsoluteUri: true } && o.BaseUrl.Scheme is "http" or "https"
                && o.BaseUrl.AbsoluteUri.EndsWith('/') && string.IsNullOrEmpty(o.BaseUrl.UserInfo)
                && string.IsNullOrEmpty(o.BaseUrl.Query) && string.IsNullOrEmpty(o.BaseUrl.Fragment)
                && o.ApiKey.Length >= 16 && o.Timeout > TimeSpan.Zero && o.Timeout <= TimeSpan.FromMinutes(10)),
            "Enabled CoachMl requires an http(s) base URL ending with /, an API key and a positive timeout.").ValidateOnStart();
        services.AddScoped<ICoachingDecisionSource, StoredCoachingDecisionSource>();
        if (!section.GetValue<bool>(nameof(CoachMlOptions.Enabled)))
            return services.AddSingleton<ICoachingReviewProvider, DisabledCoachingReviewProvider>();
        services.AddHttpClient<ICoachingReviewProvider, CoachMlClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<CoachMlOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.Timeout = options.Timeout;
            client.MaxResponseContentBufferSize = 65536;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}

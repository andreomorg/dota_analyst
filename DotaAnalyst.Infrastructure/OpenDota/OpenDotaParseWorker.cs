using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.OpenDota;

public sealed class OpenDotaParseWorker(IServiceScopeFactory scopes, IOptions<OpenDotaParsingOptions> options,
    TimeProvider clock, ILogger<OpenDotaParseWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (options.Value.Enabled)
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<OpenDotaParseStep>().RunAsync(stoppingToken);
                }
                await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("OpenDota parse worker paused after {ErrorType}", ex.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}

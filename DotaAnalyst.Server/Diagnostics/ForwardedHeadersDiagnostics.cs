namespace DotaAnalyst.Server.Diagnostics;

// Temporary troubleshooting aid: logs which client IP the API sees and the proxy headers the hosting
// platform sends, to tune ForwardedHeaders (ForwardLimit / header name). Off by default; turn on with
// Diagnostics__LogForwardedHeaders=true only while investigating, since it logs IP addresses.
public static class ForwardedHeadersDiagnostics
{
    private static readonly string[] HeaderNames =
    [
        "X-Forwarded-For",
        // Where UseForwardedHeaders moves the original X-Forwarded-For value after processing it.
        "X-Original-For",
        "X-Forwarded-Proto",
        "X-Real-IP",
        "True-Client-IP",
        "CF-Connecting-IP",
        "Forwarded",
    ];

    public static WebApplication UseForwardedHeadersDiagnostics(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Diagnostics:LogForwardedHeaders"))
        {
            return app;
        }

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ForwardedHeadersDiagnostics));
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1") && logger.IsEnabled(LogLevel.Information))
            {
                var headers = string.Join("; ", HeaderNames
                    .Where(name => context.Request.Headers.ContainsKey(name))
                    .Select(name => $"{name}={context.Request.Headers[name]}"));
                logger.LogInformation(
                    "Client IP seen: {RemoteIp}, scheme: {Scheme}, proxy headers: {Headers}",
                    context.Connection.RemoteIpAddress, context.Request.Scheme, headers);
            }

            await next(context);
        });
        return app;
    }
}

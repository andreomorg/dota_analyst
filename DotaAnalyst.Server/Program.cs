using System.Text.Json;
using System.Text.Json.Serialization;
using DotaAnalyst.Application;
using DotaAnalyst.Infrastructure;
using DotaAnalyst.Server.Authentication;
using DotaAnalyst.Server.Diagnostics;
using DotaAnalyst.Server.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    // Enums go out as camelCase strings ("goldPerMinute", "strong") instead of numbers.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddSteamAuthentication();

// Behind the hosting platform's proxy, HTTPS ends at the proxy: X-Forwarded-For/-Proto carry the real
// client IP and scheme. Only enabled where such a proxy exists (off locally), otherwise anyone could fake
// their IP and bypass the rate limit. ForwardLimit = how many proxy hops (from the right of X-Forwarded-For)
// are trusted: 1 when a single platform proxy appends the client IP. It depends on the host — check the real
// header after deploying, or every user may end up counted as the proxy's IP.
var forwardedHeadersEnabled = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (forwardedHeadersEnabled)
{
    var forwardLimit = builder.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);
    if (forwardLimit < 1)
    {
        throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be at least 1.");
    }

    // Which header carries the client IP. Default X-Forwarded-For; on Render it is CF-Connecting-IP, set by
    // the Cloudflare edge in front of it (always overwritten, so clients can't fake it, and independent of
    // how many internal proxy hops Render adds to X-Forwarded-For).
    var clientIpHeader = builder.Configuration.GetValue(
        "ForwardedHeaders:ForwardedForHeaderName", ForwardedHeadersDefaults.XForwardedForHeaderName);

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardedForHeaderName = clientIpHeader;
        options.ForwardLimit = forwardLimit;
        // The platform's proxy address is not known in advance.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (forwardedHeadersEnabled)
{
    // First, so everything after it (rate limiter, future login) sees the real IP and scheme.
    app.UseForwardedHeaders();
}

app.UseForwardedHeadersDiagnostics();

// Unexpected exceptions become a 500 ProblemDetails (with AddProblemDetails) instead of an empty body.
app.UseExceptionHandler();

app.UseDefaultFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Only in development: in the container the API listens on plain HTTP (port 8080)
    // and HTTPS is the job of a reverse proxy in front of it.
    app.UseHttpsRedirection();
}

// Before authentication, so the Steam callback (answered inside UseAuthentication) is rate limited too.
app.UseRateLimiter();

// Reads the session cookie and handles Steam's answer at /api/auth/steam/callback. After the forwarded
// headers, so it sees the real scheme (HTTPS) and host when building the address Steam sends the user back to.
app.UseAuthentication();

app.UseAuthorization();

// Every controller (the versioned business API) is rate limited; health checks are not.
app.MapControllers().RequireRateLimiting(ApiRateLimiting.PolicyName);
// Liveness only: does not call OpenDota, so the status reflects our own process, not OpenDota's.
app.MapHealthChecks("/api/health");

// Unknown /api routes return 404 instead of falling through to the React page below.
app.MapFallback("/api/{**rest}", () => Results.Problem(statusCode: StatusCodes.Status404NotFound));
app.MapFallbackToFile("/index.html");

app.Run();

// Lets the integration tests (WebApplicationFactory<Program>) reference this top-level program.
public partial class Program;

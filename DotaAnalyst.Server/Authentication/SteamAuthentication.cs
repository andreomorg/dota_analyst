using AspNet.Security.OpenId.Steam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DotaAnalyst.Server.Authentication;

// Login with Steam (OpenID 2.0) and a session cookie. Two authentication "schemes" work together:
// - Steam proves who the user is: we redirect to Steam's login page (password or QR code in the Steam app),
//   Steam sends the user back, and the handler confirms the answer with Steam itself (check_authentication),
//   so nobody can log in as someone else by editing the URL. We only ever receive the SteamID64.
// - The cookie keeps the session afterwards, so Steam is not asked again on every request.
public static class SteamAuthentication
{
    public const string CookieName = "dotaanalyst.session";

    // Technical route (outside /api/v1, like /api/health), but under /api so the Vite dev proxy forwards it.
    public const string CallbackPath = "/api/auth/steam/callback";

    // Where the browser goes when the Steam login fails or is cancelled; the React page shows a message.
    public const string LoginFailedPath = "/?login=failed";

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public static IServiceCollection AddSteamAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                // [Authorize] without a session answers 401 (cookie events below); only the login endpoint
                // explicitly goes to Steam.
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                // JavaScript can't read it, so an injected script can't steal the session.
                options.Cookie.HttpOnly = true;
                // Sent when the user arrives from another site with a normal link/redirect (like coming back
                // from Steam), but not on POSTs from other sites, which protects logout and future forms.
                options.Cookie.SameSite = SameSiteMode.Lax;
                // HTTPS-only on HTTPS requests (production, F5); plain HTTP still works for docker compose on
                // localhost. Behind Render's proxy the request counts as HTTPS thanks to X-Forwarded-Proto.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = SessionLifetime;
                // Every visit in the second half of the lifetime renews it: active users stay logged in.
                options.SlidingExpiration = true;

                // An API never redirects to a login page: it answers 401/403 and React decides what to do.
                options.Events.OnRedirectToLogin = context =>
                    WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Not logged in");
                options.Events.OnRedirectToAccessDenied = context =>
                    WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "Access denied");
            })
            .AddSteam(options =>
            {
                options.CallbackPath = CallbackPath;
                // The temporary cookie that ties Steam's answer to the login we started. Lax (default None) so it
                // also works on plain-HTTP localhost, where browsers reject SameSite=None cookies; coming back from
                // Steam is a top-level GET navigation, which Lax allows.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                // Steam confirmed the user: replace Steam's claims with ours (just the account ID).
                options.Events.OnTicketReceived = context =>
                {
                    var identifier = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (!CurrentUser.TryParseSteamIdentifier(identifier, out var accountId))
                    {
                        context.Response.Redirect(LoginFailedPath);
                        context.HandleResponse();
                        return Task.CompletedTask;
                    }

                    context.Principal = CurrentUser.CreatePrincipal(accountId, SteamAuthenticationDefaults.AuthenticationScheme);
                    return Task.CompletedTask;
                };

                // User cancelled on Steam, Steam is down, or the answer didn't check out.
                options.Events.OnRemoteFailure = context =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(SteamAuthentication).FullName!)
                        .LogWarning(context.Failure, "Steam login failed");
                    context.Response.Redirect(LoginFailedPath);
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
        return services;
    }

    private static Task WriteProblemAsync(HttpContext httpContext, int statusCode, string title)
    {
        httpContext.Response.StatusCode = statusCode;
        return httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = statusCode, Title = title },
        }).AsTask();
    }
}

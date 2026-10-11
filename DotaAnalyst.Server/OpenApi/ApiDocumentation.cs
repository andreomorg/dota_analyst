using DotaAnalyst.Server.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace DotaAnalyst.Server.OpenApi;

// The OpenAPI document (generated from the controllers by AddOpenApi) and the Scalar page that shows it.
// The transformers below fix what the generator can't know, so the document matches what the API really sends.
public static class ApiDocumentation
{
    // Both under /api: in development the Vite proxy only forwards /api to the API.
    public const string DocumentRoute = "/api/openapi/{documentName}.json";
    public const string DocsRoute = "/api/docs";

    private const string SessionCookieScheme = "sessionCookie";
    private const string ProblemDetailsSchema = "ProblemDetails";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer(async (document, context, cancellationToken) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "DotaAnalyst API",
                    Version = "v1",
                    Description =
                        "Partidas e análises de Dota 2 (dados do OpenDota). Erros sempre como ProblemDetails " +
                        "(application/problem+json): 400 entrada inválida, 401 sem login, 404 não encontrado, " +
                        "429 limite de requisições por IP, 500 erro inesperado, 502 OpenDota indisponível.\n\n" +
                        "**Login:** entre pelo site (Entrar com a Steam) nesta mesma origem; o cookie de sessão " +
                        "vai junto nas chamadas desta página.",
                };

                // The session cookie, so endpoints that need a login show it (see the operation transformer).
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[SessionCookieScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = SteamAuthentication.CookieName,
                    Description = "Sessão criada pelo login com a Steam (GET /api/v1/auth/login). HttpOnly: o navegador envia sozinho.",
                };

                // Every error response points to this schema (see FixContentTypes). Document transformers run
                // after the operations are generated, so this only adds it if no endpoint registered it.
                document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
                if (!document.Components.Schemas.ContainsKey(ProblemDetailsSchema))
                {
                    document.Components.Schemas[ProblemDetailsSchema] =
                        await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken);
                }
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                if (context.Description.ActionDescriptor is ControllerActionDescriptor)
                {
                    AddResponsesOfEveryController(operation);
                }

                FixContentTypes(operation, context.Document);

                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(SessionCookieScheme, context.Document)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }

    // Only in Development for now: the API is only used by our own React client (see plan, step 7).
    public static void MapApiDocumentation(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        app.MapOpenApi(DocumentRoute);
        app.MapScalarApiReference(DocsRoute, options => options
            .WithTitle("DotaAnalyst API")
            .WithOpenApiRoutePattern(DocumentRoute));
    }

    // Responses no action declares: every controller has the rate limit per IP (see RateLimiting/), and an
    // unexpected exception becomes a 500 ProblemDetails (UseExceptionHandler).
    private static void AddResponsesOfEveryController(OpenApiOperation operation)
    {
        operation.Responses ??= [];
        operation.Responses.TryAdd("429", new OpenApiResponse
        {
            Description = "Limite de requisições por IP excedido. Tente de novo depois do tempo do Retry-After.",
            Headers = new Dictionary<string, IOpenApiHeader>
            {
                ["Retry-After"] = new OpenApiHeader
                {
                    Description = "Segundos até a janela do limite reabrir.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
                },
            },
        });
        operation.Responses.TryAdd("500", new OpenApiResponse { Description = "Erro inesperado." });
    }

    // The generator lists every formatter MVC has (text/plain, text/json, ...). The API only answers JSON,
    // and errors always as ProblemDetails (application/problem+json), even the ones declared without a type
    // (e.g. [ProducesResponseType(502)]). Errors by the first digit, so "4XX"/"5XX" ranges count too.
    private static void FixContentTypes(OpenApiOperation operation, OpenApiDocument? document)
    {
        foreach (var (statusCode, response) in operation.Responses ?? [])
        {
            var isError = statusCode.StartsWith('4') || statusCode.StartsWith('5');
            if (response is not OpenApiResponse editable)
            {
                continue;
            }

            if (editable.Content is not { Count: > 0 } content)
            {
                if (isError)
                {
                    editable.Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference(ProblemDetailsSchema, document) },
                    };
                }

                continue;
            }

            var schema = content.Values.First().Schema;
            content.Clear();
            content[isError ? "application/problem+json" : "application/json"] = new OpenApiMediaType { Schema = schema };
        }
    }
}

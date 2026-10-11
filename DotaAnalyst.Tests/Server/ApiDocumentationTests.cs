using System.Net;
using System.Text.Json;

namespace DotaAnalyst.Tests.Server;

// The OpenAPI document must describe what the API really sends (see DotaAnalyst.Server/OpenApi/ApiDocumentation).
// The ApiFactory runs in Development, the only environment where the docs exist.
public class ApiDocumentationTests
{
    private const string DocumentUrl = "/api/openapi/v1.json";

    [Fact]
    public async Task Enums_AreCamelCaseStrings()
    {
        var schemas = (await GetDocumentAsync()).GetProperty("components").GetProperty("schemas");

        var values = schemas.GetProperty("PerformanceRating").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString());
        Assert.Equal(["weak", "average", "strong"], values);
        Assert.Contains("goldPerMinute", schemas.GetProperty("BenchmarkMetric").GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Numbers_AreOnlyIntegers()
    {
        // Not ["integer", "string"]: the API never sends numbers as text.
        var kills = (await GetDocumentAsync()).GetProperty("components").GetProperty("schemas")
            .GetProperty("MatchSummary").GetProperty("properties").GetProperty("kills");

        Assert.Equal("integer", kills.GetProperty("type").GetString());
    }

    [Fact]
    public async Task SuccessResponsesWithABody_AreJson()
    {
        foreach (var (path, method, operation) in Operations(await GetDocumentAsync()))
        {
            foreach (var response in operation.GetProperty("responses").EnumerateObject().Where(r => r.Name == "200"))
            {
                Assert.True(response.Value.TryGetProperty("content", out var content), $"{method} {path} 200 has no body");
                var media = Assert.Single(content.EnumerateObject());
                Assert.True(media.Name == "application/json", $"{method} {path} 200: {media.Name}");
                Assert.True(media.Value.TryGetProperty("schema", out _), $"{method} {path} 200 has no schema");
            }
        }
    }

    [Fact]
    public async Task ErrorResponses_AreAlwaysProblemDetails()
    {
        foreach (var (path, method, operation) in Operations(await GetDocumentAsync()))
        {
            foreach (var response in operation.GetProperty("responses").EnumerateObject().Where(r => IsError(r.Name)))
            {
                var label = $"{method} {path} {response.Name}";
                Assert.True(response.Value.TryGetProperty("content", out var content), $"{label} has no body");
                var media = Assert.Single(content.EnumerateObject());
                Assert.True(media.Name == "application/problem+json", $"{label}: {media.Name}");
                Assert.Equal("#/components/schemas/ProblemDetails", media.Value.GetProperty("schema").GetProperty("$ref").GetString());
            }
        }
    }

    [Fact]
    public async Task EveryEndpoint_DocumentsTheRateLimitAndUnexpectedErrors()
    {
        foreach (var (path, method, operation) in Operations(await GetDocumentAsync()))
        {
            var responses = operation.GetProperty("responses");
            Assert.True(responses.TryGetProperty("429", out var tooManyRequests), $"{method} {path} has no 429");
            Assert.True(tooManyRequests.GetProperty("headers").TryGetProperty("Retry-After", out _), $"{method} {path} 429 has no Retry-After");
            Assert.True(responses.TryGetProperty("500", out _), $"{method} {path} has no 500");
        }
    }

    [Fact]
    public async Task OnlyEndpointsThatNeedALogin_RequireTheSessionCookie()
    {
        var document = await GetDocumentAsync();

        var scheme = document.GetProperty("components").GetProperty("securitySchemes").GetProperty("sessionCookie");
        Assert.Equal("cookie", scheme.GetProperty("in").GetString());
        Assert.Equal("dotaanalyst.session", scheme.GetProperty("name").GetString());

        var secured = Operations(document)
            .Where(operation => operation.Operation.TryGetProperty("security", out _))
            .Select(operation => $"{operation.Method} {operation.Path}");
        Assert.Equal(["get /api/v1/me"], secured);
    }

    [Fact]
    public async Task EveryEndpoint_HasASummary()
    {
        foreach (var (path, method, operation) in Operations(await GetDocumentAsync()))
        {
            Assert.True(
                operation.TryGetProperty("summary", out var summary) && !string.IsNullOrWhiteSpace(summary.GetString()),
                $"{method} {path} has no summary");
        }
    }

    [Fact]
    public async Task DocsPage_IsServedInDevelopment()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/docs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(DocumentUrl)]
    [InlineData("/api/docs")]
    public async Task Docs_DoNotExistInProduction(string url)
    {
        using var factory = new ApiFactory(new Dictionary<string, string?> { ["environment"] = "Production" });

        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<JsonElement> GetDocumentAsync()
    {
        using var factory = new ApiFactory();

        var response = await factory.CreateClient().GetAsync(DocumentUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static bool IsError(string statusCode) => statusCode.StartsWith('4') || statusCode.StartsWith('5');

    private static IEnumerable<(string Path, string Method, JsonElement Operation)> Operations(JsonElement document) =>
        document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(method => method.Value.ValueKind == JsonValueKind.Object && method.Value.TryGetProperty("responses", out _))
                .Select(method => (path.Name, method.Name, method.Value)));
}

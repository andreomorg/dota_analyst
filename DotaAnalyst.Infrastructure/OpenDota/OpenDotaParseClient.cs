using System.Globalization;
using System.Net;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;

namespace DotaAnalyst.Infrastructure.OpenDota;

public sealed class OpenDotaParseClient(HttpClient httpClient) : IOpenDotaParseClient
{
    public Task<string> SubmitAsync(MatchId matchId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"request/{matchId.Value}", document =>
        {
            if (!document.RootElement.TryGetProperty("job", out var job)
                || !job.TryGetProperty("jobId", out var id)) throw new JsonException();
            var text = id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
                throw new JsonException();
            return number.ToString(CultureInfo.InvariantCulture);
        }, cancellationToken);

    public Task<bool> IsPendingAsync(string jobId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(jobId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            throw new ArgumentException("Invalid OpenDota job ID.", nameof(jobId));
        return SendAsync(HttpMethod.Get, $"request/{id}", document =>
        {
            if (document.RootElement.ValueKind == JsonValueKind.Null) return false;
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("jobId", out var returnedId)
                || returnedId.ToString() != jobId) throw new JsonException();
            return true;
        }, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, Func<JsonDocument, T> read,
        CancellationToken cancellationToken)
    {
        var submitting = method == HttpMethod.Post;
        try
        {
            using var request = new HttpRequestMessage(method, path);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => "rateLimited",
                    HttpStatusCode.BadRequest => "invalidRequest",
                    HttpStatusCode.NotFound => "jobMissing",
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "accessDenied",
                    _ => "serviceUnavailable",
                };
                throw new OpenDotaParseException(code, submitting && (int)response.StatusCode >= 500);
            }
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            return read(document);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new OpenDotaParseException("invalidResponse", submitting, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new OpenDotaParseException("serviceUnavailable", submitting, ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OpenDotaParseException("timeout", submitting, ex);
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotaAnalyst.Application.Abstractions;
using DotaAnalyst.Domain.Matches;
using DotaAnalyst.Domain.Replays;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotaAnalyst.Infrastructure.ReplayParser;

// The Go replay parser (replay-parser/), over HTTP. Typed HttpClient, configured in DependencyInjection; its
// request logging is turned off there, because the API key travels in a header.
//
// In production the parser runs on the project owner's machine, behind an ngrok tunnel. When that machine is
// off, ngrok itself answers (an HTML/text page with an "ngrok-error-code" header, sometimes with status 404).
// So only application/problem+json errors come from our parser and say something about the replay; anything
// else means "the parser is unavailable", never "the replay doesn't exist".
//
// The parser only parses a few replays at a time and is shared with the owner's local API, so "busy" (503 with
// Retry-After) is normal: the client waits as told (at most MaxBusyWait) and tries again, up to MaxAttempts
// requests, before giving up with ReplayParserBusyException.
//
// It asks for the whole match (POST /v1/match): the laning phase and the timeline come out of it, and the document is
// kept as it came (IReplayDocumentArchive) for the analyses still to come, so a replay is parsed once.
public class ReplayParserClient(
    HttpClient httpClient,
    IOptions<ReplayParserOptions> options,
    TimeProvider timeProvider,
    IReplayDocumentArchive archive,
    ILogger<ReplayParserClient> logger) : IReplayParser
{
    // Requests per replay while the parser answers "busy" (the first one included).
    public const int MaxAttempts = 3;

    // Longest wait between two of them, whatever Retry-After says (the worker holds the queue meanwhile).
    public static readonly TimeSpan MaxBusyWait = TimeSpan.FromSeconds(30);

    // Wait when a "busy" answer has no (usable) Retry-After.
    public static readonly TimeSpan DefaultBusyWait = TimeSpan.FromSeconds(5);

    private const string MatchPath = "v1/match";
    private const string ProblemJson = "application/problem+json";
    private const string NgrokErrorHeader = "ngrok-error-code";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReplayAnalysis> ParseAsync(MatchId matchId, Uri replayUrl, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            TimeSpan wait;
            using (var response = await SendAsync(replayUrl, cancellationToken))
            {
                if (response.IsSuccessStatusCode)
                {
                    return await ReadAnalysisAsync(response, matchId, cancellationToken);
                }

                if (!IsBusy(response))
                {
                    return await ThrowForErrorAsync(response, matchId, cancellationToken);
                }

                if (attempt == MaxAttempts)
                {
                    var detail = await ReadProblemDetailAsync(response, cancellationToken);
                    throw new ReplayParserBusyException(
                        $"The replay parser was still busy after {MaxAttempts} attempts for match {matchId}: {detail}");
                }

                wait = BusyWait(response);
            }

            logger.LogDebug("The replay parser is busy; match {MatchId} is tried again in {Wait}", matchId, wait);
            // Through the app's clock, so tests don't really wait. Cancelled when the app stops.
            await Task.Delay(wait, timeProvider, cancellationToken);
        }
    }

    // A new HttpRequestMessage each time: one can't be sent twice.
    private async Task<HttpResponseMessage> SendAsync(Uri replayUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MatchPath)
        {
            Content = JsonContent.Create(new ReplayParserRequest(replayUrl.AbsoluteUri), options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(options.Value.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        }

        // Skips ngrok's "you are about to visit" page for free domains; ignored by everything else.
        request.Headers.Add("ngrok-skip-browser-warning", "1");

        try
        {
            // Default completion option (whole body buffered): the timeout also covers reading the body.
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Connection refused, DNS, TLS... The messages never include the API key (it is only in a header).
            throw new ReplayParserUnavailableException($"Could not reach the replay parser ({ex.HttpRequestError}).", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancelled without the caller asking for it: the HttpClient timeout expired.
            throw new ReplayParserUnavailableException("The replay parser did not answer in time.", ex);
        }
    }

    // "Busy" from our parser itself; a 503 from anything else (ngrok, a proxy) is "unavailable".
    private static bool IsBusy(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.ServiceUnavailable && IsFromTheParser(response);

    // Retry-After as seconds or as a date, kept between zero and MaxBusyWait.
    private TimeSpan BusyWait(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var wait = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - timeProvider.GetUtcNow() : DefaultBusyWait);
        return wait < TimeSpan.Zero ? TimeSpan.Zero
            : wait > MaxBusyWait ? MaxBusyWait
            : wait;
    }

    private async Task<ReplayAnalysis> ReadAnalysisAsync(
        HttpResponseMessage response, MatchId matchId, CancellationToken cancellationToken)
    {
        // Something that is not our parser may answer 200 too (e.g. ngrok's warning page): that is not a reading
        // of the replay.
        if (IsFromSomethingElse(response) || response.Content.Headers.ContentType?.MediaType != "application/json")
        {
            throw new ReplayParserUnavailableException(
                $"Something other than the replay parser answered ({DescribeAnswer(response)}).");
        }

        try
        {
            var document = await response.Content.ReadAsStringAsync(cancellationToken);
            var body = JsonSerializer.Deserialize<ReplayParserMatchResponse>(document, JsonOptions)
                ?? throw new InvalidDataException("The answer is empty.");
            var analysis = body.ToReplayAnalysis(matchId);

            // Only a document that made a valid analysis is kept. Keeping it never fails the parse (the archive
            // logs and swallows its own errors).
            await archive.KeepAsync(matchId, body.SchemaVersion!.Value, document, cancellationToken);
            return analysis;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new ReplayUnreadableException($"The replay parser's answer for match {matchId} could not be used.", ex);
        }
        catch (HttpRequestException ex)
        {
            // The connection dropped (or the body was too large) while reading.
            throw new ReplayParserUnavailableException("The replay parser's answer could not be read.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ReplayParserUnavailableException("The replay parser did not answer in time.", ex);
        }
    }

    private async Task<ReplayAnalysis> ThrowForErrorAsync(
        HttpResponseMessage response, MatchId matchId, CancellationToken cancellationToken)
    {
        if (!IsFromTheParser(response))
        {
            throw new ReplayParserUnavailableException(
                $"Something other than the replay parser answered ({DescribeAnswer(response)}).");
        }

        var detail = await ReadProblemDetailAsync(response, cancellationToken);
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                throw new ReplayExpiredException($"Valve no longer has the replay of match {matchId}: {detail}");

            case HttpStatusCode.UnprocessableEntity:
                throw new ReplayUnreadableException($"The replay parser could not read the replay of match {matchId}: {detail}");

            case HttpStatusCode.BadGateway:
                // The parser is fine, but Valve's server failed the download this time: saved as failed, so it is
                // tried again later without pausing the parser for everyone else.
                throw new ReplayUnreadableException($"The replay parser could not download the replay of match {matchId}: {detail}");

            case HttpStatusCode.BadRequest:
                // We sent something the parser refuses (e.g. an address outside *.valve.net): our bug, not the
                // replay's. Saved as failed, so the same request isn't sent on every visit (a warning, not an
                // error: it comes back every FailedReplayRetryInterval while the match is visited).
                logger.LogWarning("The replay parser refused our request for match {MatchId}: {Detail}", matchId, detail);
                throw new ReplayUnreadableException($"The replay parser refused the request for match {matchId}: {detail}");

            case HttpStatusCode.Unauthorized:
                // Configuration, not the replay: nothing is saved and parsing pauses (see ReplayParserCircuitBreaker).
                logger.LogError(
                    "The replay parser refused our API key: ReplayParser:ApiKey must match its REPLAY_PARSER_API_KEY");
                throw new ReplayParserUnavailableException("The replay parser refused our API key.");

            default:
                // 500 and anything unexpected: the parser itself is in trouble; try again later.
                throw new ReplayParserUnavailableException(
                    $"The replay parser could not parse match {matchId} now ({(int)response.StatusCode}): {detail}");
        }
    }

    // ngrok adds this header to the answers it gives on its own (tunnel offline, endpoint not found...).
    private static bool IsFromSomethingElse(HttpResponseMessage response) =>
        response.Headers.Contains(NgrokErrorHeader);

    // Our parser's errors are always ProblemDetails.
    private static bool IsFromTheParser(HttpResponseMessage response) =>
        !IsFromSomethingElse(response) && response.Content.Headers.ContentType?.MediaType == ProblemJson;

    private static string DescribeAnswer(HttpResponseMessage response)
    {
        var ngrokError = response.Headers.TryGetValues(NgrokErrorHeader, out var values) ? $", {NgrokErrorHeader} {values.First()}" : "";
        return $"status {(int)response.StatusCode}, {response.Content.Headers.ContentType?.MediaType ?? "no content type"}{ngrokError}";
    }

    // The "detail" of the parser's ProblemDetails, only to make the logs useful.
    private static async Task<string> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions, cancellationToken);
            return problem?.Detail ?? problem?.Title ?? "no detail";
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return "no detail";
        }
    }

    private sealed record ProblemBody(string? Title, string? Detail);
}

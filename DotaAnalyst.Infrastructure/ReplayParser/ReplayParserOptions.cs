namespace DotaAnalyst.Infrastructure.ReplayParser;

// "ReplayParser" section of the configuration: where the Go replay parser (replay-parser/) answers.
// Without BaseUrl replay parsing is off, like Steam profiles without a key. ApiKey is a secret (user-secrets
// locally, ReplayParser__ApiKey in Docker and on Render) and must be the parser's REPLAY_PARSER_API_KEY.
public class ReplayParserOptions
{
    public const string SectionName = "ReplayParser";

    // Locally (compose) http://replay-parser:8081/; in production the ngrok tunnel's https address.
    public Uri? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    // Long: the parser downloads tens of MB from Valve and reads them (it gives up by itself after 3 minutes).
    // Only the background worker waits for it, never a visitor.
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(4);
}

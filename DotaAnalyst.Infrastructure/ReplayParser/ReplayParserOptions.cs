namespace DotaAnalyst.Infrastructure.ReplayParser;

// "ReplayParser" section of the configuration: where the Go replay parser (replay-parser/) answers.
// Without a valid BaseUrl replay parsing is off (warning in the log), like Steam profiles without a key. ApiKey is a secret (user-secrets
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

    // Keep the parser's whole raw document of each match (replay_documents, ~130 KB per match). Off by default, so
    // production (Neon) only keeps refined data; on in the compose of the project owner's machine, where heavy data
    // lives (CLAUDE.md, "Base de comparação").
    public bool KeepDocuments { get; init; }
}

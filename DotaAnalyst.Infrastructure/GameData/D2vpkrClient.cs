using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotaAnalyst.Infrastructure.GameData;

public sealed class GameDataOptions
{
    public const string SectionName = "GameData";

    // Synchronizes the builds into the local database. Only on the owner's machine (heavy work stays there).
    public bool SyncEnabled { get; set; }

    // The first build covered: the release of patch 7.41 (the owner chose 7.41 and its builds, 21/10/2026).
    public DateTimeOffset Since { get; set; } = new(2026, 3, 24, 0, 0, 0, TimeSpan.Zero);

    // How often to look for new builds.
    public TimeSpan SyncEvery { get; set; } = TimeSpan.FromHours(6);

    // Optional GitHub token (secret): 5,000 API calls an hour instead of 60. Without it a first sync takes a few hours.
    public string? GitHubToken { get; set; }
}

// GitHub said no more calls until ResetAt (60 an hour without a token).
public sealed class GitHubRateLimitedException(DateTimeOffset resetAt)
    : Exception($"GitHub API rate limit reached until {resetAt:u}")
{
    public DateTimeOffset ResetAt { get; } = resetAt;
}

// A commit of the repository that publishes the game's files (github.com/dotabuff/d2vpkr): one per client update.
public sealed record D2vpkrCommit(string Sha, DateTimeOffset Date, string Message)
{
    // "Client 6962" -> 6962.
    public int? ClientVersion => Regex.Match(Message, @"Client\s+(\d+)") is { Success: true } match
        ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
}

// Reads github.com/dotabuff/d2vpkr: the commits that touched the files we need, the blob hash of each file at a
// commit, and a file's text (raw.githubusercontent.com, which does not count against the API limit). Paths are inside
// the game's "dota" folder, as GameFiles names them.
public class D2vpkrClient(HttpClient httpClient)
{
    private const string Repository = "dotabuff/d2vpkr";
    private const string GamePrefix = "dota/";

    // The folders and files whose commits make a new build.
    private static readonly string[] WatchedPaths = ["dota/scripts/npc", "dota/resource/localization/abilities_english.txt"];

    public async Task<IReadOnlyList<D2vpkrCommit>> ListBuildsAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var commits = new Dictionary<string, D2vpkrCommit>();
        foreach (var path in WatchedPaths)
        {
            for (var page = 1; ; page++)
            {
                var url = $"https://api.github.com/repos/{Repository}/commits?path={Uri.EscapeDataString(path)}"
                    + $"&since={since.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&per_page=100&page={page}";
                using var document = await GetJsonAsync(url, cancellationToken);
                var entries = document.RootElement.EnumerateArray().ToList();
                foreach (var entry in entries)
                {
                    var commit = entry.GetProperty("commit");
                    var sha = entry.GetProperty("sha").GetString()!;
                    commits.TryAdd(sha, new D2vpkrCommit(sha, commit.GetProperty("committer").GetProperty("date").GetDateTimeOffset(),
                        commit.GetProperty("message").GetString() ?? ""));
                }

                if (entries.Count < 100)
                {
                    break;
                }
            }
        }

        return commits.Values.OrderBy(commit => commit.Date).ToList();
    }

    // The blob hash of every needed file at a commit (GameFiles.IsNeeded), by game path: three API calls.
    public async Task<IReadOnlyDictionary<string, string>> ListFilesAsync(string commit, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>();
        foreach (var folder in new[] { "dota/scripts/npc/heroes", "dota/scripts/npc" })
        {
            using var listing = await GetJsonAsync(Contents(folder, commit), cancellationToken);
            foreach (var entry in listing.RootElement.EnumerateArray().Where(entry => entry.GetProperty("type").GetString() == "file"))
            {
                var path = entry.GetProperty("path").GetString()![GamePrefix.Length..];
                if (GameFiles.IsNeeded(path))
                {
                    files[path] = entry.GetProperty("sha").GetString()!;
                }
            }
        }

        using var translation = await GetJsonAsync(Contents(GamePrefix + GameFiles.Translation, commit), cancellationToken);
        files[GameFiles.Translation] = translation.RootElement.GetProperty("sha").GetString()!;
        return files;
    }

    public async Task<string> DownloadAsync(string commit, string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"https://raw.githubusercontent.com/{Repository}/{commit}/{GamePrefix}{path}", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string Contents(string path, string commit) =>
        $"https://api.github.com/repos/{Repository}/contents/{path}?ref={commit}";

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            && response.Headers.TryGetValues("x-ratelimit-reset", out var reset)
            && long.TryParse(reset.FirstOrDefault(), out var resetSeconds))
        {
            throw new GitHubRateLimitedException(DateTimeOffset.FromUnixTimeSeconds(resetSeconds));
        }

        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }
}

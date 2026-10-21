using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Application.Models;

namespace DotaAnalyst.Tests.Application;

// The situation columns live in SituationRow (C#) and in the Python exporter (coach-ml risk/export.py), which checks
// every exported CSV against its list: this keeps the two equal and in the same order, so a column added to one can't
// be forgotten in the other (an export would only fail, or silently miss the column, after a long run).
public partial class SituationRowColumnsTests
{
    [Fact]
    public void PythonExport_ListsEverySituationRowColumn_InTheSameOrder()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "coach-ml", "src", "coach_ml", "risk", "export.py"));
        var lists = new Dictionary<string, List<string>>
        {
            ["_SITUATION_V1"] = Names(ListOf(source, "_SITUATION_V1")),
            ["_HIDDEN_V1"] = Names(ListOf(source, "_HIDDEN_V1")),
        };
        // The entry of the current version, up to the dictionary's closing brace (it is the last entry).
        var current = Regex.Match(source, $@"^\s*{SituationRow.FeatureVersion}: (?<body>.*?)^\}}",
            RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(current.Success, $"Version {SituationRow.FeatureVersion} not found in SITUATION_BY_VERSION.");
        var python = Token().Matches(current.Groups["body"].Value)
            .SelectMany(token => token.Groups["name"].Success ? [token.Groups["name"].Value] : lists[token.Groups["list"].Value]);

        // The rows go out as JSON in camelCase.
        var csharp = typeof(SituationRow).GetConstructors().Single().GetParameters()
            .Select(parameter => JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!));
        Assert.Equal(csharp, python);
    }

    private static string ListOf(string source, string name)
    {
        var match = Regex.Match(source, $@"^{name} = \[(?<body>[^\]]*)\]", RegexOptions.Multiline);
        Assert.True(match.Success, $"{name} not found in export.py.");
        return match.Groups["body"].Value;
    }

    private static List<string> Names(string body) => Token().Matches(body).Select(token => token.Groups["name"].Value).ToList();

    // The folder with the solution file, above the test's bin folder.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DotaAnalyst.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DotaAnalyst.slnx not found above the tests.");
    }

    [GeneratedRegex(@"'(?<name>\w+)'|(?<list>_SITUATION_V1|_HIDDEN_V1)")]
    private static partial Regex Token();
}

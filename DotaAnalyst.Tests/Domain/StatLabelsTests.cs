using System.Text.Json;
using System.Text.RegularExpressions;
using DotaAnalyst.Domain.Stats;

namespace DotaAnalyst.Tests.Domain;

// The catalog of statistics lives in the Domain and the Portuguese names in the React page: this keeps the two lists
// equal, so a statistic added to one can't be forgotten in the other.
public partial class StatLabelsTests
{
    [Fact]
    public void ReactLabels_HaveEveryKeyOfTheCatalog_AndNothingElse()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "dotaanalyst.client", "src", "stats", "statLabels.js"));
        var block = LabelsBlock().Match(source);
        Assert.True(block.Success, "STAT_LABELS not found in statLabels.js.");
        var labeled = LabelKey().Matches(block.Groups["body"].Value).Select(match => match.Groups["key"].Value).Order();

        // The API writes the keys in camelCase (enums as text).
        var catalog = StatCatalog.All.Select(stat => JsonNamingPolicy.CamelCase.ConvertName(stat.Key.ToString())).Order();
        Assert.Equal(catalog, labeled);
    }

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

    [GeneratedRegex(@"export const STAT_LABELS = \{(?<body>[^}]*)\}")]
    private static partial Regex LabelsBlock();

    [GeneratedRegex(@"^\s*(?<key>\w+):", RegexOptions.Multiline)]
    private static partial Regex LabelKey();
}

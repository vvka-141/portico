using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-159. Portico expands POSIX short-option clusters — `-abc` → `-a -b -c`, `-n5` → `-n 5` — and
// no user-facing document said so. The only description was the XML comment on an internal class, so
// the capability was discoverable by reading private source or by guessing.
//
// Guessing is exactly what a user does here, because every POSIX tool they already use bundles. The
// dangerous half is not what expands but what does NOT: a cluster containing an unknown letter, a map
// short carrying a [key], an assignment. Each is left whole on purpose, and a page that listed only
// the happy path would teach half a rule.
//
// So the table is executed. Each documented form is run through a real application declaring exactly
// the options the page shows, and the assertions below are what the row promises — not a token-array
// comparison against a hand-built schema, which could agree with a stale declaration forever.
public sealed class CliShortOptionDocs_Should
{
    private const string CapabilitiesPath = "docs/reference/capabilities.md";
    private const string Heading = "### Short options bundle, POSIX-style";

    /// <summary>The command the documented table is written against, declared exactly as the page shows it.</summary>
    public sealed class SyncTool
    {
        public CliFlag? All;
        public CliFlag? Verbose;
        public int Number;
        public Dictionary<string, string>? Env;

        [CliRoute("sync")]
        [CliCommandExample("sync -av")]
        public int Sync(
            [CliOption("--all|-a")] CliFlag? all = null,
            [CliOption("--verbose|-v")] CliFlag? verbose = null,
            [CliOption("--number|-n")] int number = 0,
            [CliOption("--env|-e")] Dictionary<string, string>? env = null)
        {
            All = all;
            Verbose = verbose;
            Number = number;
            Env = env;
            return 0;
        }
    }

    /// <summary>
    /// The forms the page documents, each with what its row claims. Adding a row to the table without
    /// adding a case here fails <see cref="Document_Exactly_The_Forms_These_Tests_Cover"/>.
    /// </summary>
    private static readonly string[] CoveredForms =
    [
        "-av",
        "-avn5",
        "-n5",
        "-n=5",
        "-e[region] eu",
        "-ax",
        "--all",
    ];

    public static TheoryData<string> DocumentedForms() => [.. CoveredForms];

    [Theory]
    [MemberData(nameof(DocumentedForms))]
    public void Read_Each_Documented_Short_Form_As_Written(string typed)
    {
        var tool = new SyncTool();
        var result = CliTestHarness.ForApplication(cfg => cfg.AddCommands(tool)).Run($"app sync {typed}");

        switch (typed)
        {
            case "-av":
                result.ExpectExit(0);
                Assert.NotNull(tool.All);
                Assert.NotNull(tool.Verbose);
                break;

            case "-avn5":
                // The scalar takes the remainder of the cluster — the flags before it still bind.
                result.ExpectExit(0);
                Assert.NotNull(tool.All);
                Assert.NotNull(tool.Verbose);
                Assert.Equal(5, tool.Number);
                break;

            case "-n5":
                result.ExpectExit(0);
                Assert.Equal(5, tool.Number);
                break;

            case "-n=5":
                // An assignment, not a cluster: the value is 5, not "=5". Expanding it here would
                // make argv and the string tokenizer disagree about the same token (POR-56).
                result.ExpectExit(0);
                Assert.Equal(5, tool.Number);
                break;

            case "-e[region] eu":
                // The [key] must reach the tokenizer intact; a split would tear it off (POR-58).
                result.ExpectExit(0);
                Assert.Equal("eu", Assert.IsType<Dictionary<string, string>>(tool.Env)["region"]);
                break;

            case "-ax":
                // 'x' is not declared, so nothing is guessed and the whole token is reported.
                Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
                Assert.Contains("-ax", result.StandardError, StringComparison.Ordinal);
                break;

            case "--all":
                result.ExpectExit(0);
                Assert.NotNull(tool.All);
                Assert.Null(tool.Verbose);
                break;

            default:
                Assert.Fail($"'{typed}' is in the documented set but has no assertion here.");
                break;
        }
    }

    /// <summary>
    /// The page and these tests describe the same set of forms. A row added to the table without a
    /// case above is an undocumented promise; a case here that the page omits is a capability back in
    /// the state POR-159 was filed for.
    /// </summary>
    [Fact]
    public void Document_Exactly_The_Forms_These_Tests_Cover()
    {
        var documented = TableForms().ToHashSet(StringComparer.Ordinal);
        var covered = CoveredForms.ToHashSet(StringComparer.Ordinal);

        var untested = documented.Except(covered).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var undocumented = covered.Except(documented).OrderBy(f => f, StringComparer.Ordinal).ToArray();

        Assert.True(untested.Length == 0,
            $"{CapabilitiesPath} documents {string.Join(", ", untested)} with no case in " +
            "DocumentedForms. Every example on that page is meant to be executed.");

        Assert.True(undocumented.Length == 0,
            $"These forms are tested but no longer documented: {string.Join(", ", undocumented)}. " +
            "Add the row back, or drop the case.");
    }

    /// <summary>
    /// The first column of the table under the short-option heading.
    /// </summary>
    /// <remarks>
    /// Walks lines and stops at the first non-row rather than looking for a blank-line terminator:
    /// <c>.gitattributes</c> leaves markdown CRLF on Windows, so a <c>"\n\n"</c> probe finds nothing
    /// and silently scans to end of file (POR-152).
    /// </remarks>
    private static IReadOnlyCollection<string> TableForms()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), CapabilitiesPath));

        var start = Array.FindIndex(lines, line => line.StartsWith(Heading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"{CapabilitiesPath} has no '{Heading}' section. If it was renamed, update this test — " +
            "do not delete the guard.");

        var forms = new List<string>();
        var inTable = false;

        foreach (var line in lines.Skip(start + 1))
        {
            var isRow = line.StartsWith('|');
            if (inTable && !isRow) break;
            if (!isRow) continue;
            inTable = true;

            var match = Regex.Match(line, @"^\|\s*`(?<form>[^`]+)`\s*\|");
            if (match.Success) forms.Add(match.Groups["form"].Value);
        }

        Assert.True(forms.Count > 0, $"No rows parsed out of the table in {CapabilitiesPath}.");
        return forms;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "portico.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}

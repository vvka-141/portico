using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Diagnostics;
using Portico.Analyzers;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-158. Adding an analyzer rule means editing four files, two of them nowhere near the analyzer
// code — and nothing compared them, so a rule could ship while the project's own tables said it did
// not exist.
//
// It already happened: POR-145 added POR013 and left the README table and PORTICO-FOR-AGENTS.md at
// POR011. It was caught four tickets later by POR-154, and only because that ticket's acceptance
// criteria happened to mention README consistency for an unrelated reason.
//
// The agent asset is the one that matters most. PORTICO-FOR-AGENTS.md ships INSIDE the package
// (buildTransitive/ and the package root) precisely so an agent can learn the framework's
// diagnostics without reading the source. An agent that reads a stale table meets a warning it
// cannot explain — the exact failure the asset exists to prevent.
//
// WHERE THE TRUTH COMES FROM: the analyzers' own SupportedDiagnostics, reached by reflection. The
// ticket proposed parsing AnalyzerReleases.Unshipped.md as the register, which is nearly right — the
// Roslyn release-tracking rules already force that file to match the descriptors. But "nearly right"
// via a second file is one indirection more than needed, so the manifest is checked against the code
// too (Track_Every_Declared_Diagnostic_In_The_Release_Manifest) rather than trusted as the source.
// A list declared in THIS file would be the cheap-but-wrong version: it would agree with a stale
// table forever, which is the drift being guarded against, one layer up.
public sealed class PorticoAnalyzerDocs_Should
{
    private const string ReadmePath = "README.md";
    private const string AgentsPath = "PORTICO-FOR-AGENTS.md";
    private const string RulesPath = "docs/reference/analyzer-rules.md";
    private const string ManifestPath = "src/Portico.Analyzers/AnalyzerReleases.Unshipped.md";
    private const string ShippedManifestPath = "src/Portico.Analyzers/AnalyzerReleases.Shipped.md";

    /// <summary>
    /// Every rule ID the analyzer assembly actually reports. Reflection over the assembly rather
    /// than a list of analyzer types: a new analyzer class is exactly the thing that would be
    /// forgotten, and forgetting it here would make this whole file agree with the omission.
    /// </summary>
    private static IReadOnlyCollection<string> LiveRuleIds() =>
        // Any analyzer type serves as the handle on the assembly; the descriptors themselves live on
        // an internal PorticoDiagnostics that only the code fixes can see.
        typeof(UnconvertibleOptionTypeAnalyzer).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .Select(descriptor => descriptor.Id)
            // POR009 is declared by two descriptors — one for method parameters, one for bundle
            // properties — and is one rule to a reader.
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The release manifest is what the Roslyn release-tracking rules enforce and what ships in the
    /// package; it must name exactly the rules the assembly reports. Both files are read, because a
    /// rule moves from Unshipped to Shipped when 1.0.0 cuts and this guard must survive that.
    /// </summary>
    [Fact]
    public void Track_Every_Declared_Diagnostic_In_The_Release_Manifest()
    {
        var manifested = ManifestRuleIds().ToHashSet(StringComparer.Ordinal);
        var live = LiveRuleIds().ToHashSet(StringComparer.Ordinal);

        AssertSameRules(live, manifested, ManifestPath,
            "Add a row to the '### New Rules' table.");
    }

    [Fact]
    public void List_Every_Live_Rule_In_The_Readme_Table()
    {
        var documented = TableRuleIds(ReadmePath, "## Compile-time checks, not runtime surprises");

        AssertSameRules(LiveRuleIds().ToHashSet(StringComparer.Ordinal), documented, ReadmePath,
            "Add a row to the table under '## Compile-time checks, not runtime surprises'.");
    }

    /// <summary>
    /// The agent asset ships inside the package, so a stale table here is published, not merely
    /// wrong on GitHub.
    /// </summary>
    [Fact]
    public void List_Every_Live_Rule_In_The_Agent_Asset()
    {
        var documented = TableRuleIds(AgentsPath, "## The analyzers — your edit-loop verifier");

        AssertSameRules(LiveRuleIds().ToHashSet(StringComparer.Ordinal), documented, AgentsPath,
            "Add a row to the table under '## The analyzers'.");
    }

    /// <summary>
    /// The reference page needs a section per rule. A retired ID keeps its section — POR007's
    /// section is the only place that records why there is no POR007 and why the ID is not reused —
    /// so an extra heading is allowed only when it says so on the heading line itself.
    /// </summary>
    [Fact]
    public void Document_Every_Live_Rule_In_The_Rules_Reference()
    {
        var headings = RuleHeadings();
        var live = LiveRuleIds().ToHashSet(StringComparer.Ordinal);

        var missing = live.Except(headings.Keys, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0,
            $"{RulesPath} has no section for {string.Join(", ", missing)}. Add a '## PORxxx' section.");

        foreach (var (id, heading) in headings.Where(h => !live.Contains(h.Key)))
        {
            Assert.True(heading.Contains("retired", StringComparison.OrdinalIgnoreCase),
                $"{RulesPath} documents '{id}', which no analyzer reports. If the rule was removed, " +
                $"mark the heading retired and say why the ID is not reused (as POR007 does); if it " +
                $"was renamed, fix the heading. Found: '{heading}'.");
        }
    }

    /// <summary>
    /// Rule IDs are allocated in sequence, so a gap means an ID was skipped and a repeat means one
    /// was reused. POR007 is the single documented gap. This is also the check that keeps the
    /// reference page's "the next free rule is PORxxx" sentence honest — the sentence POR-145's
    /// ID-collision comment tells the next author to trust, and which was itself stale when this
    /// guard was written.
    /// </summary>
    [Fact]
    public void Allocate_Rule_Ids_In_An_Unbroken_Sequence()
    {
        var numbers = LiveRuleIds().Select(id => int.Parse(id.Substring(3))).OrderBy(n => n).ToArray();
        var expected = Enumerable.Range(1, numbers.Max())
            .Where(n => n != 7)     // POR007, retired and never reused
            .ToArray();

        Assert.Equal(expected, numbers);

        var nextFree = $"POR{numbers.Max() + 1:D3}";
        var claim = File.ReadAllLines(Path.Combine(RepositoryRoot(), RulesPath))
            .FirstOrDefault(line => line.Contains("next free rule", StringComparison.OrdinalIgnoreCase));

        Assert.True(claim is not null,
            $"{RulesPath} no longer states the next free rule ID. That sentence is where the next " +
            "author looks before allocating one — keep it, and keep this guard.");

        Assert.True(claim!.Contains(nextFree, StringComparison.Ordinal),
            $"{RulesPath} says the next free rule is something other than {nextFree}: '{claim!.Trim()}'.");
    }

    private static void AssertSameRules(
        IReadOnlySet<string> live,
        IReadOnlyCollection<string> documented,
        string path,
        string howToFix)
    {
        var listed = documented.ToHashSet(StringComparer.Ordinal);

        var missing = live.Except(listed).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var phantom = listed.Except(live).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0,
            $"{path} does not list {string.Join(", ", missing)}, which the analyzers report. {howToFix}");

        Assert.True(phantom.Length == 0,
            $"{path} lists {string.Join(", ", phantom)}, which no analyzer reports. Remove the row — " +
            "a rule a user cannot trigger is worse than no documentation at all.");
    }

    /// <summary>Rule IDs in the first column of the table that follows <paramref name="heading"/>.</summary>
    /// <remarks>
    /// Walks lines and stops at the first non-row, rather than looking for a blank-line terminator:
    /// <c>.gitattributes</c> leaves markdown CRLF on Windows, so a <c>"\n\n"</c> probe finds nothing
    /// and silently scans to end of file. That cost POR-152 a debugging session, and every table in
    /// this repository is parsed the same way because of it.
    /// </remarks>
    private static IReadOnlyCollection<string> TableRuleIds(string path, string heading)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), path));

        var start = Array.FindIndex(lines, line => line.StartsWith(heading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"{path} has no '{heading}' section. If it was renamed, update this test — do not " +
            "delete the guard.");

        var ids = new List<string>();
        var inTable = false;

        foreach (var line in lines.Skip(start + 1))
        {
            var isRow = line.StartsWith('|');
            if (inTable && !isRow) break;
            if (!isRow) continue;
            inTable = true;

            // Backticked in the README, bare in the agent asset.
            var match = Regex.Match(line, @"^\|\s*`?(?<id>POR\d{3})`?\s*\|");
            if (match.Success) ids.Add(match.Groups["id"].Value);
        }

        Assert.True(ids.Count > 0, $"No rule rows parsed out of the table in {path}.");
        return ids;
    }

    /// <summary>Rule ID → the full heading line, so a retired marker can be checked.</summary>
    private static Dictionary<string, string> RuleHeadings()
    {
        var headings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(Path.Combine(RepositoryRoot(), RulesPath)))
        {
            var match = Regex.Match(line, @"^##\s+(?<id>POR\d{3})\b");
            if (match.Success) headings[match.Groups["id"].Value] = line;
        }

        return headings;
    }

    private static IReadOnlyCollection<string> ManifestRuleIds()
    {
        var ids = new List<string>();

        foreach (var path in new[] { ManifestPath, ShippedManifestPath })
        {
            foreach (var line in File.ReadAllLines(Path.Combine(RepositoryRoot(), path)))
            {
                var match = Regex.Match(line, @"^(?<id>POR\d{3})\s*\|");
                if (match.Success) ids.Add(match.Groups["id"].Value);
            }
        }

        return ids;
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

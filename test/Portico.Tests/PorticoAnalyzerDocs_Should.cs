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
    private const string AuditPath = "docs/explanation/analyzer-message-audit.md";
    private const string ManifestPath = "src/Portico.Analyzers/AnalyzerReleases.Unshipped.md";
    private const string ShippedManifestPath = "src/Portico.Analyzers/AnalyzerReleases.Shipped.md";

    /// <summary>
    /// Every rule ID the analyzer assembly actually reports. Reflection over the assembly rather
    /// than a list of analyzer types: a new analyzer class is exactly the thing that would be
    /// forgotten, and forgetting it here would make this whole file agree with the omission.
    /// </summary>
    private static IReadOnlyCollection<string> LiveRuleIds() => PorticoAnalyzerRules.LiveIds();

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
    /// The message-actionability audit scores every rule's message against four criteria. A rule
    /// missing from its summary table has shipped a message nobody assessed — and that is not
    /// hypothetical: POR012 shipped and never appeared there at all, while POR013 shipped and got a
    /// header note conceding it "has not been assessed". A standard that silently exempts the newest
    /// rules is not a standard, so the table is compared against the analyzers rather than read.
    /// </summary>
    /// <remarks>
    /// This is the same guard the three tables above get. It is separate only because the audit's
    /// first column is a markdown link (<c>[POR012](#por012)</c>) rather than a bare or backticked
    /// ID, so <see cref="TableRuleIds"/>'s pattern does not match it.
    /// </remarks>
    [Fact]
    public void Assess_Every_Live_Rule_In_The_Message_Audit()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), AuditPath));

        var assessed = lines
            .Select(line => Regex.Match(line, @"^\|\s*\[(?<id>POR\d{3})\]\(#por\d{3}\)\s*\|"))
            .Where(match => match.Success)
            .Select(match => match.Groups["id"].Value)
            .ToArray();

        Assert.True(assessed.Length > 0,
            $"No rule rows parsed out of the summary table in {AuditPath}. If the table moved or " +
            "changed shape, update this test — do not delete the guard.");

        AssertSameRules(LiveRuleIds().ToHashSet(StringComparer.Ordinal), assessed, AuditPath,
            "Assess the message against the four criteria and add a row to the '## Summary' table, " +
            "plus a '## PORxxx' section with the per-criterion verdict.");

        // A row in the summary is a link to a section; a summary entry without one is a dead anchor.
        var sections = lines
            .Select(line => Regex.Match(line, @"^##\s+(?<id>POR\d{3})\b"))
            .Where(match => match.Success)
            .Select(match => match.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var dangling = assessed.Where(id => !sections.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.True(dangling.Length == 0,
            $"{AuditPath} scores {string.Join(", ", dangling)} in the summary table but has no " +
            "'## PORxxx' section for it, so the row links nowhere and the verdict has no reasoning " +
            "behind it.");
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

    /// <summary>
    /// The reference page's "Code fix (Ctrl-.)" column must name exactly the rules a provider offers
    /// to fix — an em dash where there is none.
    /// </summary>
    /// <remarks>
    /// POR-122 added this column and three providers in one commit, and the page's prose immediately
    /// disagreed with the page's own table: the table listed seven rules with a fix while three
    /// sentences on the same page, one in the message audit and three in the changelog all said six.
    /// Nothing compared the column to the code, so the count was arithmetic done by hand over a set —
    /// the failure shape the coverage notes single out as this repository's most productive.
    /// <para>
    /// A promised fix that does not exist is the worse direction: the reader reaches for Ctrl-. and
    /// finds nothing, having been told the rule is the helpful kind. Both directions are asserted.
    /// </para>
    /// </remarks>
    [Fact]
    public void Mark_Every_Rule_That_Has_A_Code_Fix_In_The_Rules_Table()
    {
        var documented = TableCodeFixCells(RulesPath)
            .Where(cell => cell.Value != "—")
            .Select(cell => cell.Key)
            .ToArray();

        var fixable = PorticoAnalyzerRules.FixableIds().ToHashSet(StringComparer.Ordinal);

        AssertSameRules(fixable, documented, RulesPath,
            "Put the fix's one-line description in the 'Code fix (Ctrl-.)' column, or an em dash " +
            "(—) if the rule offers none.",
            phantomHint:
                "No provider offers a fix for it, so the column promises a lightbulb the reader will " +
                "press Ctrl-. and not find. Put an em dash (—) in the cell, or write the provider.");
    }

    /// <summary>
    /// Every live rule's own section has to state whether it ships a fix. The page promises exactly
    /// this — "Each rule below says which it is and why" — and the promise was false for POR006,
    /// which has a fix its section never mentioned.
    /// </summary>
    [Fact]
    public void State_The_Code_Fix_Status_In_Every_Rule_Section()
    {
        var silent = SectionBodies(RulesPath)
            .Where(section => PorticoAnalyzerRules.LiveIds().Contains(section.Key))
            .Where(section => !section.Value.Contains("code fix", StringComparison.OrdinalIgnoreCase))
            .Select(section => section.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.True(silent.Length == 0,
            $"{RulesPath} never says whether {string.Join(", ", silent)} ships a code fix, though the " +
            "page's introduction promises each rule does. Say which it is — and for a rule that " +
            "offers none, why, because 'a fix that guesses is worse than no fix' is the reasoning a " +
            "reader is owed.");
    }

    /// <summary>
    /// The prose count — "<i>seven</i> of the <i>twelve</i> ship a code fix" — must match the code.
    /// </summary>
    /// <remarks>
    /// This one is deliberately coupled to a sentence pattern, which the rest of this file avoids.
    /// The justification is that the sentence is exactly what rotted, in three documents at once, and
    /// a table gate alone would not have caught it. If the wording moves away from "N of the M …
    /// code fix", this test fails loudly rather than passing vacuously — update it, do not delete it.
    /// <para>
    /// The sibling numbers in the same paragraphs ("all seven support Fix all", "the other five offer
    /// nothing") are <b>not</b> gated. They are derivable from these two, and a regex per sentence
    /// would couple this guard to prose faster than the prose changes.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(RulesPath)]
    [InlineData(AuditPath)]
    public void Count_The_Code_Fixes_Correctly_In_Prose(string path)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), path));
        var claims = Regex.Matches(text, @"\b(?<count>[A-Za-z]+) of the (?<total>[A-Za-z]+)\b[^.]*?code fix",
            RegexOptions.IgnoreCase);

        Assert.True(claims.Count > 0,
            $"{path} no longer states how many rules ship a code fix in the form 'N of the M … code " +
            "fix'. That sentence is what went stale when POR-122 shipped three fixes — if it was " +
            "reworded, update this test rather than deleting the guard.");

        var expectedCount = NumberWord(PorticoAnalyzerRules.FixableIds().Count);
        var expectedTotal = NumberWord(PorticoAnalyzerRules.LiveIds().Count);

        foreach (Match claim in claims)
        {
            Assert.True(
                string.Equals(claim.Groups["count"].Value, expectedCount, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(claim.Groups["total"].Value, expectedTotal, StringComparison.OrdinalIgnoreCase),
                $"{path} says '{claim.Value.Trim()}'. There are {expectedCount} rules with a code fix " +
                $"out of {expectedTotal} live rules. Update the sentence — and the numbers beside it " +
                "in the same paragraph, which this guard does not read.");
        }
    }

    private static readonly string[] NumberWords =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen", "twenty",
    ];

    // Past twenty the docs would spell it with a digit anyway, and a rule set that large is a
    // different conversation than this guard is having.
    private static string NumberWord(int value) =>
        value >= 0 && value < NumberWords.Length
            ? NumberWords[value]
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Rule ID → the text of its "Code fix (Ctrl-.)" cell, located by the header's name.</summary>
    /// <remarks>
    /// The column is found by reading the header rather than by a fixed index, so inserting a column
    /// moves the guard with the table instead of silently reading the wrong cell.
    /// </remarks>
    private static Dictionary<string, string> TableCodeFixCells(string path)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), path));
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        var column = -1;

        foreach (var line in lines)
        {
            if (!line.StartsWith('|')) continue;

            var parts = line.Split('|').Select(part => part.Trim()).ToArray();

            if (column < 0)
            {
                column = Array.FindIndex(parts, part =>
                    part.StartsWith("Code fix", StringComparison.OrdinalIgnoreCase));
                continue;
            }

            var match = Regex.Match(parts.Length > 1 ? parts[1] : string.Empty, @"^\[?(?<id>POR\d{3})\]?");
            if (match.Success && column < parts.Length)
            {
                cells[match.Groups["id"].Value] = parts[column];
            }
        }

        Assert.True(column >= 0,
            $"{path} has no 'Code fix' column in its rule table. If it was renamed, update this test " +
            "— do not delete the guard.");
        Assert.True(cells.Count > 0, $"No rule rows parsed out of the rule table in {path}.");

        return cells;
    }

    /// <summary>Rule ID → everything between its <c>## PORxxx</c> heading and the next one.</summary>
    private static Dictionary<string, string> SectionBodies(string path)
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = string.Empty;
        var body = new List<string>();

        void Flush()
        {
            if (current.Length > 0) bodies[current] = string.Join("\n", body);
            body.Clear();
        }

        foreach (var line in File.ReadAllLines(Path.Combine(RepositoryRoot(), path)))
        {
            var match = Regex.Match(line, @"^##\s+(?<id>POR\d{3})\b");
            if (match.Success)
            {
                Flush();
                current = match.Groups["id"].Value;
                continue;
            }

            body.Add(line);
        }

        Flush();
        return bodies;
    }

    /// <summary>
    /// Asserts <paramref name="documented"/> and <paramref name="live"/> name the same rules, in both
    /// directions, with a message that says what to do about each.
    /// </summary>
    /// <remarks>
    /// <c>phantomHint</c> overrides the "no analyzer reports it" wording for a caller comparing
    /// something other than the rule set. A failure message is a diagnostic, and the code-fix
    /// column's phantom direction means a fix was <i>promised</i> and is absent — not that the rule
    /// does not exist.
    /// </remarks>
    private static void AssertSameRules(
        IReadOnlySet<string> live,
        IReadOnlyCollection<string> documented,
        string path,
        string howToFix,
        string? phantomHint = null)
    {
        var listed = documented.ToHashSet(StringComparer.Ordinal);

        var missing = live.Except(listed).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var phantom = listed.Except(live).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0,
            $"{path} does not list {string.Join(", ", missing)}, which the analyzers report. {howToFix}");

        Assert.True(phantom.Length == 0,
            $"{path} lists {string.Join(", ", phantom)}. " + (phantomHint ??
                "No analyzer reports it — remove the row, because a rule a user cannot trigger is " +
                "worse than no documentation at all."));
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

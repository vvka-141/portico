using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Portico.Analyzers;
using Portico.Testing;
using Xunit;

namespace Portico;

/// <summary>
/// Cross-document checks for public claims that previously drifted while the narrower README and
/// reference-page gates stayed green.
/// </summary>
public sealed class PublicDocumentation_Should
{
    private static readonly string Root = RepositoryPaths.Root();

    [Fact]
    public void Name_Every_CliTestRunResult_Assertion_Exactly()
    {
        const string path = "docs/reference/capabilities.md";
        var text = File.ReadAllText(Path.Combine(Root, path));
        var section = Between(text, "### End-to-end testing — `CliTestHarness`", "## See also");

        var documented = Regex.Matches(section, @"`(?<name>Expect[A-Za-z]+)`")
            .Cast<Match>()
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var shipped = typeof(CliTestRunResult)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.StartsWith("Expect", StringComparison.Ordinal))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(shipped, documented);
    }

    [Fact]
    public void Classify_Every_Analyzer_Runtime_Backstop()
    {
        const string path = "docs/reference/analyzer-rules.md";
        var lines = File.ReadAllLines(Path.Combine(Root, path));
        var header = lines.Single(line =>
            line.StartsWith("| Rule |", StringComparison.Ordinal) &&
            line.Contains("Runtime backstop", StringComparison.Ordinal));
        var column = Array.FindIndex(
            header.Split('|').Select(cell => cell.Trim()).ToArray(),
            cell => cell == "Runtime backstop");
        Assert.True(column > 0, "The analyzer table has no Runtime backstop column.");

        var documented = lines
            .Where(line => Regex.IsMatch(line, @"^\| \[POR\d{3}\]"))
            .ToDictionary(
                line => Regex.Match(line, @"POR\d{3}").Value,
                line => line.Split('|')[column].Trim(),
                StringComparer.Ordinal);
        var withoutBackstop = new HashSet<string>(["POR004", "POR012", "POR013"], StringComparer.Ordinal);

        foreach (var rule in PorticoAnalyzerRules.LiveIds())
        {
            Assert.True(documented.TryGetValue(rule, out var value), $"{path} has no row for {rule}.");
            Assert.StartsWith(withoutBackstop.Contains(rule) ? "No" : "Yes", value, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(
            "Every rule has a runtime backstop",
            File.ReadAllText(Path.Combine(Root, path)),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>dotnet new portico-cli</c> scaffolds onto the <b>newest</b> target framework Portico
    /// supports, not the oldest.
    /// </summary>
    /// <remarks>
    /// This rule was briefly the opposite — "default to the minimum supported framework" — as a fix
    /// for a real bug: the tutorial promised ".NET 8 SDK or later" while the template defaulted to
    /// <c>net10.0</c>, so an SDK-8-only machine scaffolded a project it could not build. The bug was
    /// real and the remedy was aimed at the wrong file.
    /// <para>
    /// <b>Defaulting to the oldest supported target points every new project at the runtime closest
    /// to death.</b> On the day this was written, <c>net8.0</c> was in <i>maintenance</i> with support
    /// ending 2026-11-10 — 102 days out — while <c>net10.0</c> was the <i>active</i> LTS supported to
    /// 2028-11-14. A scaffolding default is the one place that matters most, because it is chosen for
    /// people who have not formed an opinion yet, and it is frozen into a NuGet page until the next
    /// release. The tutorial's prerequisite is what needed correcting, and was corrected.
    /// </para>
    /// <para>
    /// Newest-supported is asserted rather than a named TFM so this does not need editing when the
    /// set moves, and it stays deterministic — no network, no dated snapshot. Whether a supported
    /// target has entered maintenance is a different question, answered weekly and against live data
    /// by <c>eng/check-dotnet-lifecycle.sh</c> (POR-146), which raises it for a human to decide.
    /// </para>
    /// </remarks>
    [Fact]
    public void Default_The_Template_To_The_Newest_Supported_Framework()
    {
        var props = XDocument.Load(Path.Combine(Root, "Directory.Build.props"));
        var supported = props.Descendants("TargetFrameworks")
            .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Where(tfm => Regex.IsMatch(tfm, @"^net\d+\.\d+$"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(FrameworkVersion)
            .ToArray();

        Assert.NotEmpty(supported);

        var configPath = Path.Combine(
            Root,
            "templates/Portico.Templates/content/PorticoCli/.template.config/template.json");
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        var defaultFramework = config.RootElement
            .GetProperty("symbols")
            .GetProperty("Framework")
            .GetProperty("defaultValue")
            .GetString();

        Assert.Equal(supported[^1], defaultFramework);

        var readme = File.ReadAllText(Path.Combine(
            Root,
            "templates/Portico.Templates/PACKAGE-README.md"));
        Assert.Contains(
            $"default target is `{defaultFramework}`",
            readme,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Use_The_Live_Analyzer_Count_In_Public_Prose()
    {
        var expected = PorticoAnalyzerRules.LiveIds().Count;
        const string number =
            "zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|" +
            "fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|\\d+";
        var pattern = new Regex(
            $@"(?:\b(?:all|the|there are)\s+(?<count>{number})\s+live\s+(?:rules|diagnostics)\b|" +
            $@"\b(?:the\s+)?(?<count>{number})\s+compile-time checks\b|" +
            $@"\bPortico's\s+(?<count>{number})\s+rules\b|" +
            $@"\bof the\s+(?<count>{number})\s+rules\b)",
            RegexOptions.IgnoreCase);

        var stale = new List<string>();
        foreach (var path in PublicMarkdownFiles())
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(path)))
            {
                if (ParseNumber(match.Groups["count"].Value) != expected)
                {
                    stale.Add($"{Path.GetRelativePath(Root, path)}: '{match.Value}'");
                }
            }
        }

        Assert.True(
            stale.Count == 0,
            $"Public documentation uses a stale analyzer count; there are {expected} live rules:" +
            Environment.NewLine + string.Join(Environment.NewLine, stale.Select(item => $"  {item}")));
    }

    [Fact]
    public void Call_Stale_Examples_Test_Failures_Not_Build_Failures()
    {
        var pattern = new Regex(
            @"(?:stale (?:example|one).{0,80}fails? the build|example.{0,100}build goes red|fails? the build.{0,100}stops dispatching)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var paths = PublicMarkdownFiles().Concat(Directory.EnumerateFiles(
            Path.Combine(Root, "templates"),
            "template.json",
            SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)));

        var failures = paths
            .Select(path => (Path: path, Match: pattern.Match(File.ReadAllText(path))))
            .Where(result => result.Match.Success)
            .Select(result => $"{Path.GetRelativePath(Root, result.Path)}: '{result.Match.Value}'")
            .ToArray();

        Assert.True(
            failures.Length == 0,
            "A stale executable example fails only when CliContractValidator runs in the test " +
            "suite; reserve 'build failure' for compiler/analyzer failures:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures.Select(item => $"  {item}")));
    }

    [Fact]
    public void Avoid_Literal_Package_Versions_In_The_Template_Readme()
    {
        const string path = "templates/Portico.Templates/PACKAGE-README.md";
        var text = File.ReadAllText(Path.Combine(Root, path));
        var literal = Regex.Match(
            text,
            @"(?:Portico(?:\.Templates)?\s+|--porticoVersion\s+)\d+\.\d+\.\d+",
            RegexOptions.IgnoreCase);

        Assert.False(
            literal.Success,
            $"{path} hard-codes '{literal.Value}'. The README is packed unchanged; use " +
            "'<version>' or describe the package-relative default instead.");
    }

    [Fact]
    public void Resolve_Every_Local_Markdown_Anchor()
    {
        var failures = new List<string>();
        var linkPattern = new Regex(@"\[[^\]]*\]\((?<target>[^)]+)\)");

        foreach (var source in PublicMarkdownFiles())
        {
            foreach (Match link in linkPattern.Matches(File.ReadAllText(source)))
            {
                var target = link.Groups["target"].Value.Trim().Trim('<', '>');
                if (!target.Contains("#", StringComparison.Ordinal) ||
                    target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = target.Split('#', 2);
                var targetPath = parts[0].Length == 0
                    ? source
                    : Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(source)!,
                        Uri.UnescapeDataString(parts[0])));
                var anchor = Uri.UnescapeDataString(parts[1]).ToLowerInvariant();

                if (!File.Exists(targetPath)) continue; // The existing link gate reports this case.
                if (!MarkdownAnchors(targetPath).Contains(anchor))
                {
                    failures.Add(
                        $"{Path.GetRelativePath(Root, source)} -> {target} " +
                        $"(no '#{anchor}' in {Path.GetRelativePath(Root, targetPath)})");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Local Markdown links contain missing heading anchors:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures.Select(item => $"  {item}")));
    }

    private static IEnumerable<string> PublicMarkdownFiles()
    {
        yield return Path.Combine(Root, "README.md");
        yield return Path.Combine(Root, "PORTICO-FOR-AGENTS.md");

        foreach (var directory in new[] { "docs", "examples", "src", "templates" })
        {
            foreach (var path in Directory.EnumerateFiles(
                         Path.Combine(Root, directory),
                         "*.md",
                         SearchOption.AllDirectories)
                     .Where(path => !path.Contains(
                         $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.Ordinal))
                     .Where(path => !path.Contains(
                         $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                         StringComparison.Ordinal)))
            {
                yield return path;
            }
        }
    }

    private static string Between(string text, string start, string end)
    {
        var first = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(first >= 0, $"Could not find section start '{start}'.");

        var last = text.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(last > first, $"Could not find section end '{end}'.");
        return text[first..last];
    }

    private static HashSet<string> MarkdownAnchors(string path)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            var heading = Regex.Match(line, @"^#{1,6}\s+(?<text>.+?)\s*#*\s*$");
            if (!heading.Success) continue;

            var text = heading.Groups["text"].Value;
            text = Regex.Replace(text, @"!?\[(?<label>[^\]]*)\]\([^)]+\)", "${label}");
            text = Regex.Replace(text, @"<[^>]+>", string.Empty);
            text = text.Replace("`", string.Empty, StringComparison.Ordinal)
                .Replace("*", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            text = Regex.Replace(text, @"[^\p{L}\p{N}\s_-]", string.Empty);
            var slug = Regex.Replace(text, @"\s", "-").Trim('-');

            duplicates.TryGetValue(slug, out var count);
            duplicates[slug] = count + 1;
            anchors.Add(count == 0 ? slug : $"{slug}-{count}");
        }

        return anchors;
    }

    private static Version FrameworkVersion(string tfm) =>
        Version.Parse(tfm.Substring("net".Length));

    private static int ParseNumber(string value)
    {
        if (int.TryParse(value, out var number)) return number;

        var words = new[]
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
            "seventeen", "eighteen", "nineteen", "twenty",
        };

        return Array.FindIndex(words, word => string.Equals(word, value, StringComparison.OrdinalIgnoreCase));
    }
}

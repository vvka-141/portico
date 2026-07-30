using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Portico.Reflection;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-152. The framework's widest capability — 17 collection shapes, more than any surveyed
// competitor binds — was documented nowhere. Its only description in the repository was an XML
// comment on an internal class, so the only ways to discover it were reading private source or
// guessing a type and seeing whether it worked. Given POR-144 (shapes that look supported and fail
// at dispatch), guessing is exactly what a user should not have to do.
//
// The section now exists. This is the part that keeps it true.
//
// A framework whose central claim is that documentation cannot drift must not ship a hand-maintained
// reference table — that would be the framework's own thesis, unenforced, on its own docs. So the
// table is compared against SupportedCollectionDefinitions itself rather than against a list this
// test declares: a test-declared list would agree with a stale table forever, which is the drift
// being guarded against, one layer up.
public sealed class CliCollectionBindingDocs_Should
{
    private const string CapabilitiesPath = "docs/reference/capabilities.md";

    /// <summary>The type names in the first column of the shapes table, e.g. <c>List&lt;T&gt;</c>.</summary>
    /// <remarks>
    /// Walks lines rather than searching for a blank-line terminator: the repository's
    /// <c>.gitattributes</c> leaves markdown with CRLF endings on Windows, so a <c>"\n\n"</c> probe
    /// finds nothing and silently scans to end of file — which read the <c>CliFlag?</c> table three
    /// sections down as collection shapes. Ending the table at the first non-row line has no such
    /// failure mode.
    /// </remarks>
    private static IReadOnlyCollection<string> DocumentedShapes()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root(), CapabilitiesPath));

        var heading = Array.FindIndex(lines, line =>
            line.StartsWith("#### The shapes that bind", StringComparison.Ordinal));

        Assert.True(heading >= 0,
            $"{CapabilitiesPath} has no '#### The shapes that bind' section. If it was renamed, " +
            "update this test — do not delete the guard.");

        var shapes = new List<string>();
        var inTable = false;

        foreach (var line in lines.Skip(heading + 1))
        {
            var isRow = line.StartsWith('|');
            if (inTable && !isRow) break;      // first line past the table ends it
            if (!isRow) continue;              // prose between the heading and the table
            inTable = true;

            var match = Regex.Match(line, @"^\|\s*`(?<type>[^`]+)`\s*\|");
            if (match.Success) shapes.Add(match.Groups["type"].Value);
        }

        Assert.True(shapes.Count > 0, $"No shape rows parsed out of the table in {CapabilitiesPath}.");
        return shapes;
    }

    /// <summary>
    /// The allow-list as the framework holds it, rendered the way the docs spell it. Arrays are a
    /// separate branch in <c>GetCollectionItemType</c> rather than an entry in the set, so they are
    /// added here — the one place the two representations legitimately differ.
    /// </summary>
    private static IReadOnlyCollection<string> SupportedShapes() =>
        CliCollectionOptionMaterializer.SupportedCollectionDefinitions
            .Select(definition => $"{definition.Name[..definition.Name.IndexOf('`')]}<T>")
            .Append("T[]")
            .ToArray();

    [Fact]
    public void Document_Exactly_The_Shapes_The_Framework_Binds()
    {
        var documented = DocumentedShapes().ToHashSet(StringComparer.Ordinal);
        var supported = SupportedShapes().ToHashSet(StringComparer.Ordinal);

        var missing = supported.Except(documented).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var phantom = documented.Except(supported).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0,
            $"{CapabilitiesPath} does not document {missing.Length} shape(s) the framework binds: " +
            $"{string.Join(", ", missing)}. Add a row to the table in '#### The shapes that bind'.");

        Assert.True(phantom.Length == 0,
            $"{CapabilitiesPath} documents {phantom.Length} shape(s) the framework does NOT bind: " +
            $"{string.Join(", ", phantom)}. That is a promise the code does not keep — remove the row, " +
            "or add the shape to SupportedCollectionDefinitions.");
    }

    /// <summary>
    /// The count in the prose is a claim like any other. It appears in the ticket, in the README and
    /// in competitive copy ("more shapes than any of the six surveyed frameworks"), so it is exactly
    /// the sort of number that goes stale and gets quoted.
    /// </summary>
    [Fact]
    public void Bind_Seventeen_Shapes()
    {
        Assert.Equal(17, SupportedShapes().Count);
    }

    /// <summary>
    /// The table's <b>Duplicates</b> column, executed. Every shape the framework claims is bound for
    /// real and checked against what its own row promises — so the page is not merely consistent with
    /// the allow-list, it is consistent with the framework's behaviour.
    /// </summary>
    /// <remarks>
    /// This is the reference page held to the standard the framework sells: an example that is
    /// executed rather than asserted once by hand. It also closed a live gap — five of the seventeen
    /// shapes (<c>IEnumerable&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>,
    /// <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>) had no round-trip test at
    /// all when POR-152 was written, contrary to the ticket's assumption that
    /// <c>CliCollectionTypes_Should</c> already covered one per shape.
    /// </remarks>
    [Fact]
    public void Bind_Every_Documented_Shape_As_Its_Row_Promises()
    {
        var rows = DocumentedRows();

        foreach (var shape in SupportedShapes())
        {
            var declaredType = shape == "T[]"
                ? typeof(string[])
                : DefinitionNamed(shape).MakeGenericType(typeof(string));

            Assert.True(rows.TryGetValue(shape, out var dedupes),
                $"'{shape}' binds but has no row in the table.");

            var probeType = typeof(CliCollectionTypes_Should.AbsentProbe<>).MakeGenericType(declaredType);
            object? probe = null;

            Portico.Testing.CliTestHarness
                .ForApplication(cfg => cfg.AddCommands(
                    probeType,
                    () => probe = Activator.CreateInstance(probeType)!,
                    []))
                .Run("app run --tags a b a")
                .ExpectExit(0);

            var bound = probeType
                .GetField(nameof(CliCollectionTypes_Should.AbsentProbe<int>.Received))!
                .GetValue(probe);

            var values = Assert.IsAssignableFrom<IEnumerable<string>>(bound).ToArray();

            Assert.Contains("a", values);
            Assert.Contains("b", values);
            Assert.True(values.Length == (dedupes ? 2 : 3),
                $"'{shape}' is documented as {(dedupes ? "deduping" : "keeping")} duplicates, but " +
                $"binding 'a b a' produced {values.Length} value(s): {string.Join(", ", values)}.");
        }
    }

    /// <summary>Shape name → whether its row claims duplicates are removed.</summary>
    private static Dictionary<string, bool> DocumentedRows()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root(), CapabilitiesPath));
        var heading = Array.FindIndex(lines, line =>
            line.StartsWith("#### The shapes that bind", StringComparison.Ordinal));

        var rows = new Dictionary<string, bool>(StringComparer.Ordinal);
        var inTable = false;

        foreach (var line in lines.Skip(heading + 1))
        {
            var isRow = line.StartsWith('|');
            if (inTable && !isRow) break;
            if (!isRow) continue;
            inTable = true;

            var match = Regex.Match(line, @"^\|\s*`(?<type>[^`]+)`\s*\|[^|]*\|(?<dupes>[^|]*)\|");
            if (match.Success)
            {
                rows[match.Groups["type"].Value] =
                    match.Groups["dupes"].Value.Contains("deduped", StringComparison.OrdinalIgnoreCase);
            }
        }

        return rows;
    }

    private static Type DefinitionNamed(string shape) =>
        CliCollectionOptionMaterializer.SupportedCollectionDefinitions
            .Single(d => $"{d.Name[..d.Name.IndexOf('`')]}<T>" == shape);
}

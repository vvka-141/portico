using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Portico;

/// <summary>
/// Portico's public API is the <c>Cli*</c> types and nothing else. Every helper inherited from the
/// origin library is <c>internal</c> — a user of a CLI framework should never see a reflection
/// decorator or a string extension. Asserted rather than assumed: `internal` is one keyword away
/// from `public`, and nothing else would catch the slip.
/// </summary>
public sealed class Portico_PublicSurface_Should
{
    private static IReadOnlyList<Type> ExportedTypes =>
        Assembly.Load("Portico").GetExportedTypes();

    /// <summary>
    /// The one public type whose name does not start with <c>Cli</c>. It is the default
    /// <see cref="ICliConsole"/> singleton, it is public in the origin, and the public XML docs
    /// point at <c>SystemCliConsole.Instance</c>. POR-12's "Cli* only" rule and CLAUDE.md's
    /// "type names are inherited unchanged" rule disagree about it; the port changed nothing and
    /// the conflict is raised on POR-12 for the operator to settle. If the answer is "rename it",
    /// this entry disappears — it is not a licence to add more.
    /// </summary>
    private static readonly string[] InheritedNonCliNames = ["SystemCliConsole"];

    [Fact]
    public void ExposeOnlyCliTypes()
    {
        var offenders = ExportedTypes
            .Where(t => !t.Name.StartsWith("Cli", StringComparison.Ordinal)
                     && !t.Name.StartsWith("ICli", StringComparison.Ordinal)
                     && !InheritedNonCliNames.Contains(t.Name))
            .Select(t => t.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Public surface must be Cli* types only, but these are exported: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void ExposeOnlyPorticoNamespaces()
    {
        var offenders = ExportedTypes
            .Select(t => t.Namespace ?? "<global>")
            .Distinct()
            .Where(ns => ns != "Portico" && !ns.StartsWith("Portico.", StringComparison.Ordinal))
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Every public type must live under the Portico root namespace, but found: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void ShipTheTestingSurfaceInTheCorePackage()
    {
        // Portico.Testing is the differentiator (examples-are-tests). It ships inside the core
        // package, not behind a second `dotnet add package` — so it must be exported from here.
        Assert.Contains(ExportedTypes, t => t.FullName == "Portico.Testing.CliTestHarness");
        Assert.Contains(ExportedTypes, t => t.Name == "CliContractValidator`1");
    }

    [Fact]
    public void NotExposeTheOptionMaterializerSeam()
    {
        // ROADMAP item C4, resolved: CliOptionMaterializer stays internal. There is no
        // WithMaterializer<T>. The extension points a user needs already exist — [TypeConverter]
        // (the BCL's own), and subclassing CliOptionAttribute to override CanAccept.
        //
        // This test exists because ExposeOnlyCliTypes would NOT catch a regression here:
        // "CliOptionMaterializer" starts with "Cli", so it would sail through that check. Exposing
        // a seam later is additive and safe; removing one is breaking — so the decision is pinned.
        var leaked = ExportedTypes
            .Where(t => t.Name.Contains("Materializer", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        Assert.True(
            leaked.Count == 0,
            $"The option-materializer seam must stay internal (ROADMAP C4). Now exported: {string.Join(", ", leaked)}. " +
            "If you mean to expose it, reopen C4 with the named user scenario that requires it.");

        Assert.DoesNotContain(
            typeof(CliApplication).Assembly.GetType("Portico.ICliApplicationBuilder")!.GetMethods(),
            m => m.Name.Contains("Materializer", StringComparison.Ordinal));
    }

    [Fact]
    public void KeepTheInheritedTypeNames()
    {
        // CLAUDE.md: type names are inherited unchanged from the origin. `using Portico;` →
        // `CliApplication.Create(...)`. Renaming these to Portico* would be churn that buys nothing.
        string[] expected =
        [
            "CliApplication", "CliRouteAttribute", "CliOptionAttribute", "CliArgumentAttribute",
            "CliFlag", "CliOptions", "CliMiddleware", "CliCommandExampleAttribute",
            "ICliConsole", "CliPrompt", "CliExitException",
        ];

        var exported = ExportedTypes.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(expected, name => Assert.Contains(name, exported));
    }
}

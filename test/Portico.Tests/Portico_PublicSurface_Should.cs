using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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

    private static readonly Assembly[] AllShippedAssemblies =
    [
        typeof(CliApplication).Assembly,
        typeof(DependencyInjection.CliApplicationBuilderExtensions).Assembly,
        typeof(Hosting.CliHostExtensions).Assembly,
    ];

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

    private const string SurfaceDocPath = "docs/explanation/public-surface.md";

    /// <summary>
    /// <c>public-surface.md</c> claims to classify every exported type, and
    /// <see cref="Track_every_exported_type_by_name"/> tells its next editor to update that document
    /// too. An instruction is not a gate: the identical arrangement in the analyzer tables let POR013
    /// ship while the README and the agent asset still said POR011, and it took four tickets to
    /// notice. Both directions are checked — a type missing a row is undocumented surface, and a row
    /// with no type is a promise about something a user cannot reach.
    /// </summary>
    [Fact]
    public void Classify_Every_Exported_Type_In_The_Public_Surface_Doc()
    {
        var documented = SurfaceDocRows();

        Assert.True(documented.Count > 0,
            $"No type rows parsed out of {SurfaceDocPath}. If the tables changed shape, update this " +
            "test — do not delete the guard.");

        var exported = AllShippedAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Select(SimpleName)
            .ToHashSet(StringComparer.Ordinal);

        var undocumented = exported.Except(documented.Keys, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var phantom = documented.Keys.Except(exported, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.True(undocumented.Length == 0,
            $"{SurfaceDocPath} does not classify {string.Join(", ", undocumented)}. Every exported " +
            "type needs a row with its Kind, Tag and the reason it is public.");

        Assert.True(phantom.Length == 0,
            $"{SurfaceDocPath} classifies {string.Join(", ", phantom)}, which nothing exports. " +
            "Remove the row — documented surface a user cannot reach is worse than none.");
    }

    /// <summary>
    /// The doc's <c>Kind</c> column records whether each type is sealed, abstract or static — the same
    /// facts <c>Portico_Extensibility_Should</c> pins in code. Here they are checked as prose against
    /// reflection, so sealing <c>CliOptionAttribute</c> (or unsealing <c>CliRouteAttribute</c>) cannot
    /// leave the document quietly describing the opposite.
    /// </summary>
    [Fact]
    public void Record_The_Right_Kind_For_Every_Exported_Type()
    {
        var byName = AllShippedAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .ToDictionary(SimpleName, type => type, StringComparer.Ordinal);

        var wrong = new List<string>();

        foreach (var (name, kind) in SurfaceDocRows())
        {
            if (!byName.TryGetValue(name, out var type)) continue;   // the other test reports these

            var actual = ActualKind(type);
            var claimed = ClaimedKind(kind);

            if (!string.Equals(actual, claimed, StringComparison.Ordinal))
            {
                wrong.Add($"{name}: documented as '{kind}' (reads as {claimed}), actually {actual}");
            }
        }

        Assert.True(wrong.Count == 0,
            $"{SurfaceDocPath}'s Kind column disagrees with the assemblies:{Environment.NewLine}" +
            string.Join(Environment.NewLine, wrong.Select(line => "  " + line)));
    }

    /// <summary>Type name → the text of the <c>Kind</c> column, from every table in the doc.</summary>
    private static Dictionary<string, string> SurfaceDocRows()
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(Path.Combine(RepositoryRoot(), SurfaceDocPath)))
        {
            // | `CliApplication` | sealed class | primitive | The entry point. … |
            var match = Regex.Match(line, @"^\|\s*`(?<name>[A-Za-z]\w*)(?<generic><[^`]*>)?`\s*\|\s*(?<kind>[^|]+?)\s*\|");
            if (!match.Success) continue;

            var kind = match.Groups["kind"].Value;

            // The classification-key table at the top has the same shape but describes tags, not
            // types; its first column is a bolded tag rather than a backticked type name, so it does
            // not match. Anything whose Kind cell is not a type kind is skipped for the same reason.
            if (!LooksLikeATypeKind(kind)) continue;

            rows[match.Groups["name"].Value] = kind;
        }

        return rows;
    }

    private static bool LooksLikeATypeKind(string kind) =>
        kind.Contains("class", StringComparison.Ordinal) ||
        kind.Contains("interface", StringComparison.Ordinal) ||
        kind.Contains("record", StringComparison.Ordinal) ||
        kind.Contains("struct", StringComparison.Ordinal) ||
        kind.Contains("enum", StringComparison.Ordinal) ||
        kind.Contains("delegate", StringComparison.Ordinal);

    /// <summary>
    /// The doc's <c>Kind</c> text reduced to the one fact reflection can confirm. The record/struct
    /// vocabulary is deliberately collapsed away: whether a type is a <c>record</c> is a syntax
    /// choice with no reliable reflection answer, while sealed/abstract/static is the extensibility
    /// claim and is exactly what the charter cares about.
    /// </summary>
    private static string ClaimedKind(string kind) =>
        kind.Contains("interface", StringComparison.Ordinal) ? "interface"
        : kind.Contains("enum", StringComparison.Ordinal) ? "enum"
        : kind.Contains("struct", StringComparison.Ordinal) ? "struct"
        : kind.Contains("static", StringComparison.Ordinal) ? "static class"
        : kind.Contains("sealed", StringComparison.Ordinal) ? "sealed class"
        : kind.Contains("abstract", StringComparison.Ordinal) ? "abstract class"
        : "open class";

    private static string ActualKind(Type type) =>
        type.IsInterface ? "interface"
        : type.IsEnum ? "enum"
        : type.IsValueType ? "struct"
        // A static class is `abstract sealed` in IL; check that pairing before either alone.
        : type is { IsAbstract: true, IsSealed: true } ? "static class"
        : type.IsSealed ? "sealed class"
        : type.IsAbstract ? "abstract class"
        : "open class";

    /// <summary><c>CliContractValidator`1</c> → <c>CliContractValidator</c>, which is how docs spell it.</summary>
    private static string SimpleName(Type type) =>
        type.Name.IndexOf('`') is var tick && tick >= 0 ? type.Name[..tick] : type.Name;

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

    /// <summary>
    /// POR-104: adding or removing an exported type is a deliberate act — it must update both this
    /// list and docs/explanation/public-surface.md. The test covers all shipped assemblies (core +
    /// adapters), not just the core, because consumer NuGet references resolve all three.
    /// </summary>
    /// <remarks>
    /// The "and the document" half of that instruction is no longer advisory —
    /// <see cref="Classify_Every_Exported_Type_In_The_Public_Surface_Doc"/> enforces it. This list
    /// stays because it is the deliberate-act tripwire: it fails on a surface change even when the
    /// author dutifully updated the document, which is the case the doc check alone would wave
    /// through.
    /// </remarks>
    [Fact]
    public void Track_every_exported_type_by_name()
    {
        // Canonical list — sorted by FullName. Update docs/explanation/public-surface.md when
        // you add or remove an entry here.
        var expected = new SortedSet<string>(StringComparer.Ordinal)
        {
            // Portico (core) — framework entry point and builder
            "Portico.CliApplication",
            "Portico.ICliApplicationBuilder",
            "Portico.CliVersionBuilder",
            "Portico.CliHelpBuilder",

            // Portico (core) — attributes
            "Portico.CliRouteAttribute",
            "Portico.CliOptionAttribute",
            "Portico.CliArgumentAttribute",
            "Portico.CliCommandExampleAttribute",

            // Portico (core) — runtime types
            "Portico.CliFlag",
            "Portico.CliOptions",
            "Portico.CliMiddleware",
            "Portico.CliInvocation",
            "Portico.ICliConsole",
            "Portico.SystemCliConsole",
            "Portico.CliPrompt",
            "Portico.CliExitException",
            "Portico.CliConfigurationException",

            // Portico (core) — ready-made middleware
            "Portico.CliTimingMiddleware",
            "Portico.CliTracingMiddleware",

            // Portico (core) — shell completion
            "Portico.Completion.CliCompletion",
            "Portico.Completion.CliCompletionShell",

            // Portico (core) — option captures
            "Portico.ICliOptionCapture",
            "Portico.CliOptionCapture",
            "Portico.CliScalarOptionCapture",
            "Portico.CliFlagOptionCapture",
            "Portico.CliCollectionOptionCapture",
            "Portico.ICliCollectionCapture",
            "Portico.ICliMapOptionCapture",
            "Portico.CliKeyValueOptionCapture",
            "Portico.CliKeyFlagOptionCapture",
            "Portico.CliKeyCollectionOptionCapture",

            // Portico.Testing
            "Portico.Testing.CliTestHarness",
            "Portico.Testing.CliTestRunResult",
            "Portico.Testing.CliContractValidator`1",
            "Portico.Testing.CliContractExample",
            "Portico.Testing.CliTestAssertionException",

            // Portico.DependencyInjection
            "Portico.DependencyInjection.CliApplicationBuilderExtensions",

            // Portico.Hosting
            "Portico.Hosting.CliHostExtensions",
        };

        var actual = new SortedSet<string>(
            AllShippedAssemblies
                .SelectMany(a => a.GetExportedTypes())
                .Select(t => t.FullName!),
            StringComparer.Ordinal);

        var added = new SortedSet<string>(actual, StringComparer.Ordinal);
        added.ExceptWith(expected);

        var removed = new SortedSet<string>(expected, StringComparer.Ordinal);
        removed.ExceptWith(actual);

        Assert.True(
            added.Count == 0 && removed.Count == 0,
            "Exported surface changed. Update this list AND docs/explanation/public-surface.md." +
            (added.Count > 0
                ? Environment.NewLine + "ADDED:   " + string.Join(", ", added)
                : "") +
            (removed.Count > 0
                ? Environment.NewLine + "REMOVED: " + string.Join(", ", removed)
                : ""));
    }

    // ---------------------------------------------------------------------------------------------
    // Member-level surface (POR-83 §2). Track_every_exported_type_by_name pins the set of exported
    // TYPES; these pin the two member-level rules that audit settled. POR-104 deliberately stopped at
    // types, and the four members POR-83 named were invisible to it — as was a fifth, dead one that
    // enumerating the surface found and the ticket's hand-written list did not.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A member whose signature needs a <c>System.Reflection</c> type belongs to the reflection
    /// pipeline, not to the user — an attribute author never holds a <see cref="ParameterInfo"/>,
    /// because inside an attribute declaration there is nothing to get one from.
    /// </summary>
    /// <remarks>
    /// This is the rule POR-83 §2 resolves the audit's member list with, and it is the member-level
    /// form of the argument that ticket makes best: <c>CliApplication</c> is sealed and the
    /// materializer seam is SEALED (POR-36), so a public surface of pipeline mechanics contradicts a
    /// deliberately narrow extensibility story. The members that take a <see cref="string"/> or a
    /// <see cref="Type"/> stay public, because a subclass of <c>CliOptionAttribute</c> can genuinely
    /// use those — this narrows what nobody can call correctly, not what nobody happens to call.
    /// </remarks>
    [Fact]
    public void Keep_Reflection_Typed_Members_Off_The_Public_Surface()
    {
        // Recursive, because the namespace of `IReadOnlyList<ParameterInfo>` is
        // System.Collections.Generic. A top-level-only check passed a member returning exactly that,
        // which is the shape the rule most needs to catch: the members this rule removed were
        // *collections* of reflection objects as often as bare ones, and the natural way to re-add one
        // is inside a list. Element types are unwrapped for the same reason (`ParameterInfo[]`, `ref`).
        static IEnumerable<Type> Expand(Type type)
        {
            var core = type.HasElementType ? type.GetElementType() ?? type : type;
            yield return core;

            foreach (var argument in core.GetGenericArguments())
            {
                foreach (var nested in Expand(argument)) yield return nested;
            }
        }

        static bool IsReflectionType(Type type) =>
            Expand(type).Any(part => part.Namespace == "System.Reflection");

        var offenders = AllShippedAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly)
                .Select(member => (Type: type, Member: member)))
            .Where(entry => SignatureTypes(entry.Member).Any(IsReflectionType))
            .Select(entry => $"{entry.Type.FullName}.{entry.Member.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These exported members expose a System.Reflection type, which makes framework plumbing " +
            "part of the public contract: " + string.Join(", ", offenders) + ". Make them internal. " +
            "If one is genuinely for a subclass to call, say so in docs/explanation/public-surface.md " +
            "and add it to this test's allow-list deliberately — there is no allow-list today, and " +
            "that is the point.");
    }

    /// <summary>
    /// Every genuinely-mutable public property on the exported surface, and why it is allowed to be
    /// one. A settable property the framework populates by reflection is a seam whose every use is
    /// wrong, which is what POR-83 §2 called the strongest removal candidate on its list.
    /// </summary>
    /// <remarks>
    /// <c>init</c> accessors are excluded: they are how a positional record and an attribute named
    /// argument are both spelled, and they cannot be written after construction, so they are not a
    /// mutation seam. Distinguishing them is the whole reason this test reads the
    /// <c>IsExternalInit</c> modreq rather than <c>SetMethod.IsPublic</c> — the naive check reports
    /// twenty-three properties, twenty of them <c>init</c> and most of those record positional
    /// parameters, burying the three listed below.
    /// <para>
    /// Those counts were measured on 2026-07-30. The sentence previously read "twenty properties,
    /// seventeen of them records, and buries the four that matter", which was wrong on every figure
    /// and did not even add up — twenty minus seventeen is three, and three is what the allow-list
    /// has always held. Only the last number is enforced, by the allow-list itself; the other two
    /// illustrate why the modreq check is worth its awkwardness and will drift.
    /// </para>
    /// </remarks>
    [Fact]
    public void Allow_Only_The_Documented_Mutable_Properties()
    {
        var allowed = new SortedSet<string>(StringComparer.Ordinal)
        {
            // The display form of an argument in help output: `[CliArgument("path", Name = "PATH")]`.
            // An attribute named argument REQUIRES a settable property, and this one is the user's to
            // choose. ParameterName — the resolved name, which is not theirs — is internal set.
            "Portico.CliArgumentAttribute.Name",

            // A CliOptions bundle binds by SetValue on each property, and a middleware IS a bundle
            // (CliMiddleware : CliOptions). These two are the framework's own worked examples of that,
            // so their setters are the binding mechanism, not a leak.
            "Portico.CliTimingMiddleware.Timing",
            "Portico.CliTracingMiddleware.Level",
        };

        var actual = new SortedSet<string>(
            AllShippedAssemblies
                .SelectMany(assembly => assembly.GetExportedTypes())
                .SelectMany(type => type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly)
                    .Where(property => property.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
                    .Select(property => $"{type.FullName}.{property.Name}")),
            StringComparer.Ordinal);

        var added = new SortedSet<string>(actual, StringComparer.Ordinal);
        added.ExceptWith(allowed);

        var gone = new SortedSet<string>(allowed, StringComparer.Ordinal);
        gone.ExceptWith(actual);

        Assert.True(
            added.Count == 0 && gone.Count == 0,
            "The set of mutable public properties changed." +
            (added.Count > 0
                ? Environment.NewLine + "NEW MUTABLE: " + string.Join(", ", added) +
                  Environment.NewLine + "A settable property the framework fills in is a mutation seam. " +
                  "Prefer `internal set`, or `init` if it is set once at construction. If it must be " +
                  "public, add it here with the reason."
                : "") +
            (gone.Count > 0
                ? Environment.NewLine + "NO LONGER MUTABLE: " + string.Join(", ", gone) +
                  Environment.NewLine + "Drop the entry."
                : ""));
    }

    /// <summary>The types appearing in a member's signature — parameters plus return/property/field type.</summary>
    private static IEnumerable<Type> SignatureTypes(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
        ConstructorInfo ctor => ctor.GetParameters().Select(p => p.ParameterType),
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        _ => [],
    };

    /// <summary>
    /// True for an <c>init</c> accessor. The C# compiler marks one with a required custom modifier of
    /// <c>System.Runtime.CompilerServices.IsExternalInit</c> on the setter's return; there is no
    /// reflection API that reports "init" directly.
    /// </summary>
    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(modifier => modifier.Name == "IsExternalInit");
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Portico;

/// <summary>
/// The charter's §4.7 invariant — "<c>CliApplication</c> stays <c>sealed</c>. One way to extend:
/// implement the contract, configure via <see cref="ICliApplicationBuilder"/>" — is a claim about a
/// single keyword, repeated in three documents and, until this file, enforced by none of them.
/// Deleting <c>sealed</c> compiles, ships, and opens a second extensibility dimension that the
/// charter says creates a public contract too hard to evolve. Nothing else in the suite would notice.
/// </summary>
public sealed class Portico_Extensibility_Should
{
    private static readonly Assembly[] AllShippedAssemblies =
    [
        typeof(CliApplication).Assembly,
        typeof(DependencyInjection.CliApplicationBuilderExtensions).Assembly,
        typeof(Hosting.CliHostExtensions).Assembly,
    ];

    /// <summary>
    /// The types a user is invited to inherit from — one row each in the "What you can extend"
    /// table of <c>docs/explanation/extensibility.md</c>. Adding a name here is a decision to open
    /// a new extensibility dimension, which the charter reserves for a wall a real user hit today.
    /// </summary>
    private static readonly string[] DocumentedExtensionPoints =
    [
        "Portico.CliOptionAttribute",
        "Portico.CliArgumentAttribute",
        "Portico.CliOptions",
        "Portico.CliMiddleware",
    ];

    /// <summary>
    /// Not an extension point: the abstract base of the six parsed-option shapes. Consumers
    /// pattern-match on the concrete captures (all sealed); nobody adds a seventh. It is unsealed
    /// only because an abstract base cannot be sealed, so it is listed separately from the
    /// invitations above rather than blurred into them.
    /// </summary>
    private static readonly string[] ClosedHierarchyBases =
    [
        "Portico.CliOptionCapture",
    ];

    [Fact]
    public void Keep_CliApplication_sealed_with_one_way_to_construct_it()
    {
        var application = typeof(CliApplication);

        Assert.True(
            application.IsSealed,
            "CHARTER §4.7: CliApplication stays sealed. Inheritance plus configuration are two " +
            "extensibility dimensions, and the charter rejects the pair. If config cannot solve a " +
            "real user's problem, add an opt-in primitive — do not unseal.");

        var publicConstructors = application.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.True(
            publicConstructors.Length == 0,
            "extensibility.md: 'There is exactly one way to produce one: CliApplication.Create(cfg => …)'. " +
            $"Now public: {string.Join(", ", publicConstructors.Select(c => c.ToString()))}.");
    }

    [Fact]
    public void Seal_every_exported_class_except_the_declared_extension_points()
    {
        var allowed = DocumentedExtensionPoints
            .Concat(ClosedHierarchyBases)
            .ToHashSet(StringComparer.Ordinal);

        // Static classes are `abstract sealed` in IL, so IsSealed already excludes them; interfaces,
        // enums and structs are not classes. What survives is genuinely open for inheritance.
        IReadOnlyList<string> open = AllShippedAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsClass && !type.IsSealed)
            .Select(type => type.FullName!)
            .Where(name => !allowed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            open.Count == 0,
            $"These exported classes are open for inheritance but are not declared extension points: {string.Join(", ", open)}. " +
            "Seal them, or — if the inheritance is deliberate — add the type to DocumentedExtensionPoints " +
            "AND give it a row in docs/explanation/extensibility.md's 'What you can extend' table.");
    }

    [Fact]
    public void Document_every_extension_point_in_the_extensibility_guide()
    {
        var guide = File.ReadAllText(
            Path.Combine(RepositoryPaths.Root(), "docs", "explanation", "extensibility.md"));

        Assert.All(DocumentedExtensionPoints, fullName =>
        {
            var simpleName = fullName[(fullName.LastIndexOf('.') + 1)..];
            Assert.True(
                guide.Contains(simpleName, StringComparison.Ordinal),
                $"{fullName} is inheritable but docs/explanation/extensibility.md never names it. " +
                "An undocumented extension point is a hook nobody asked for — the charter's §4.7 " +
                "definition of speculative extensibility.");
        });
    }
}

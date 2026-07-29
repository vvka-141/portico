using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Portico;

/// <summary>
/// The wedge, asserted. CHARTER §6.5: "<c>CliContractValidator&lt;T&gt;</c> runs all examples as
/// tests. No <c>[CliCommandExample]</c> ships untested. The examples-are-tests feature is the
/// central agent-friendly mechanism and must be exercised on every shipped example."
/// </summary>
/// <remarks>
/// <para>
/// POR004 already guarantees that every <c>[CliRoute]</c> carries a <c>[CliCommandExample]</c> — the
/// analyzer fails the build without one. Nothing guaranteed the other half: that the example is
/// actually <em>executed</em>. Each example project's contract test names its interfaces by hand
/// (<c>new CliContractValidator&lt;IFleetTool&gt;()</c>), so a sixth contract added to
/// <c>examples/</c> would ship with declared-but-never-dispatched examples and every gate would stay
/// green. That is the one regression Portico cannot afford to ship, because it is the claim.
/// </para>
/// <para>
/// Parsed with Roslyn rather than grepped for <c>[CliRoute]</c>: the question is which <em>type</em>
/// declares a routed method, and a regex answers that only by guessing at brace nesting.
/// </para>
/// </remarks>
public sealed class ExamplesAreTests_Should
{
    [Fact]
    public void Validate_Every_Contract_Shipped_In_The_Examples()
    {
        var examples = Path.Combine(RepositoryRoot(), "examples");

        var contracts = SourceFiles(examples, tests: false)
            .SelectMany(TypesDeclaringRoutes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            contracts.Count > 0,
            "No routed types found under examples/. The scan is broken, or the examples moved — " +
            "either way this guard is passing vacuously. Fix it; do not delete it.");

        var validated = SourceFiles(examples, tests: true)
            .SelectMany(ValidatedContractNames)
            .ToHashSet(StringComparer.Ordinal);

        var unvalidated = contracts.Where(name => !validated.Contains(name)).ToList();

        Assert.True(
            unvalidated.Count == 0,
            $"These example contracts declare [CliRoute] methods but no CliContractValidator<T> ever " +
            $"runs them: {string.Join(", ", unvalidated)}. Every [CliCommandExample] in examples/ must " +
            "be dispatched through the real pipeline — that is the framework's central claim, and an " +
            "example nobody executes is exactly the free-text example Portico exists to replace. Add " +
            "a validator to the matching *.Tests project.");
    }

    private static IEnumerable<string> SourceFiles(string examplesRoot, bool tests) =>
        Directory.EnumerateFiles(examplesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => IsInTestProject(path, examplesRoot) == tests);

    /// <summary>A path under <c>examples/Something.Tests/</c>.</summary>
    private static bool IsInTestProject(string path, string examplesRoot) =>
        Path.GetRelativePath(examplesRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]
            .EndsWith(".Tests", StringComparison.Ordinal);

    /// <summary>Names of the types that declare at least one <c>[CliRoute]</c> method.</summary>
    private static IEnumerable<string> TypesDeclaringRoutes(string path) =>
        Root(path)
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(type => type.Members
                .OfType<MethodDeclarationSyntax>()
                .Any(method => method.AttributeLists.Any(list => list.Attributes.Any(IsCliRoute))))
            .Select(type => type.Identifier.Text);

    private static bool IsCliRoute(AttributeSyntax attribute) =>
        attribute.Name.ToString() is "CliRoute" or "CliRouteAttribute"
            or "Portico.CliRoute" or "Portico.CliRouteAttribute";

    /// <summary>The <c>T</c> of every <c>CliContractValidator&lt;T&gt;</c> mentioned in the file.</summary>
    private static IEnumerable<string> ValidatedContractNames(string path) =>
        Root(path)
            .DescendantNodes()
            .OfType<GenericNameSyntax>()
            .Where(name => name.Identifier.Text == "CliContractValidator")
            .SelectMany(name => name.TypeArgumentList.Arguments)
            .Select(argument => argument switch
            {
                // `CliContractValidator<Storage.IStorageTool>` names the same contract as
                // `CliContractValidator<IStorageTool>`; compare on the simple name either way.
                QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
                SimpleNameSyntax simple => simple.Identifier.Text,
                _ => argument.ToString(),
            });

    private static SyntaxNode Root(string path) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

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

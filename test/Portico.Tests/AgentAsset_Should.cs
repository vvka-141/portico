using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Portico.Analyzers;
using Xunit;

namespace Portico;

/// <summary>
/// <c>PORTICO-FOR-AGENTS.md</c> ships <b>inside</b> the package — the package root and
/// <c>buildTransitive/</c> — so an agent can learn the framework without reading the source. Its
/// "A complete, working tool" section says of its own code block: <i>"This compiles, dispatches,
/// and is contract-tested."</i> That is a falsifiable claim in a published file, and nothing checked
/// it.
/// </summary>
/// <remarks>
/// The asymmetry is what makes this worth a gate rather than a read-through. The README's first
/// block is compiled and run through every analyzer (<see cref="Readme_Should"/>), and
/// <c>PACKAGE-README.md</c>'s sample is a compiled project (POR-155) — but the asset that ships
/// inside the package, and is the one an agent is pointed at, had neither. The three existing tests
/// that mention it check that it is packaged and that its analyzer TABLE is current; none of them
/// looks at its code.
/// <para>
/// Only the "complete, working tool" block is verified, and the two reasons for that differ. Most
/// other blocks in the file are deliberate fragments — an attribute on its own, a signature with no
/// type around it — and making those compile would mean adapting them here, which reintroduces the
/// second source of truth this extraction exists to avoid.
/// </para>
/// <para>
/// The xUnit contract-test block immediately below this one is <b>not</b> a fragment, and its
/// absence here is a real gap rather than a considered exclusion. It is a complete test class, and
/// verifying it would prove the "and is contract-tested" third of the section's claim, which nothing
/// currently does; it needs xUnit metadata references, a different reference set from
/// <see cref="AnalyzerTestRunner"/>'s. Named rather than quietly implied to be covered.
/// </para>
/// </remarks>
// ReSharper disable once InconsistentNaming
public sealed class AgentAsset_Should
{
    private const string AssetPath = "PORTICO-FOR-AGENTS.md";
    private const string Heading = "## A complete, working tool";

    /// <summary>
    /// The first fenced <c>csharp</c> block under <see cref="Heading"/>, verbatim.
    /// </summary>
    private static string WorkedExample()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root(), AssetPath));

        var heading = Array.FindIndex(lines, line => line.StartsWith(Heading, StringComparison.Ordinal));
        Assert.True(heading >= 0,
            $"{AssetPath} has no '{Heading}' section. If it was renamed, update this test — do not " +
            "delete the guard, because the section's whole promise is that its code works.");

        var start = Array.FindIndex(lines, heading + 1, line => line.StartsWith("```csharp", StringComparison.Ordinal));
        Assert.True(start > heading, $"{AssetPath}'s '{Heading}' section has no ```csharp block.");

        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("```", StringComparison.Ordinal));
        Assert.True(end > start, $"{AssetPath}'s first ```csharp fence under '{Heading}' is never closed.");

        return string.Join(Environment.NewLine, lines[(start + 1)..end]);
    }

    private static CSharpCompilation Compile() =>
        CSharpCompilation.Create(
            assemblyName: "AgentAssetWorkedExample",
            syntaxTrees: [CSharpSyntaxTree.ParseText(WorkedExample())],
            references: AnalyzerTestRunner.MetadataReferences,
            // A library, so the block's `Main` compiles as an ordinary method and the block needs no
            // adaptation — same reasoning as Readme_Should.
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Fact]
    public void Compile_Its_Worked_Example()
    {
        var errors = Compile()
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"{AssetPath}'s worked example does not compile, though the section says it does:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(e =>
                $"  {e.Id} @ {e.Location.GetLineSpan().StartLinePosition}: {e.GetMessage()}")));
    }

    /// <summary>
    /// Every analyzer the package ships, run over the code an agent copies first.
    /// </summary>
    /// <remarks>
    /// An agent that copies this block gets whatever it trips on its own first build, and the
    /// scaffolded template sets <c>TreatWarningsAsErrors</c> — so a POR012 here is an error for the
    /// reader even though it is a Warning to us. It is also a credibility problem specific to this
    /// file: the section immediately below it is the one that teaches the analyzers.
    /// </remarks>
    [Fact]
    public async Task Satisfy_Every_Analyzer_It_Documents()
    {
        var analyzers = typeof(UnconvertibleOptionTypeAnalyzer).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .ToImmutableArray();

        Assert.False(analyzers.IsEmpty, "No analyzers discovered — the guard would pass vacuously.");

        var diagnostics = await Compile()
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync();

        Assert.True(
            diagnostics.IsEmpty,
            $"{AssetPath}'s worked example trips Portico's own analyzers:" + Environment.NewLine +
            string.Join(Environment.NewLine, diagnostics.Select(d => $"  {d.Id}: {d.GetMessage()}")));
    }
}

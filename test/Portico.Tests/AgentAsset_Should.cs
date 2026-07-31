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
/// Both blocks in that section are verified — the worked tool, and the xUnit contract test beneath
/// it, which is compiled <i>against</i> the tool because it names <c>IGreeter</c> from it. Between
/// them they cover all three parts of the sentence: it compiles, it dispatches (the analyzers agree
/// the routes and examples are well-formed), and the contract test that proves the dispatch is
/// itself real code.
/// </para>
/// <para>
/// The other blocks in the file are deliberate fragments — an attribute on its own, a signature with
/// no type around it — and making those compile would mean adapting them here, which reintroduces
/// the second source of truth this extraction exists to avoid.
/// </para>
/// </remarks>
// ReSharper disable once InconsistentNaming
public sealed class AgentAsset_Should
{
    private const string AssetPath = "PORTICO-FOR-AGENTS.md";
    private const string Heading = "## A complete, working tool";

    /// <summary>
    /// The <paramref name="index"/>-th (0-based) fenced <c>csharp</c> block under
    /// <see cref="Heading"/>, verbatim.
    /// </summary>
    private static string CodeBlock(int index)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root(), AssetPath));

        var heading = Array.FindIndex(lines, line => line.StartsWith(Heading, StringComparison.Ordinal));
        Assert.True(heading >= 0,
            $"{AssetPath} has no '{Heading}' section. If it was renamed, update this test — do not " +
            "delete the guard, because the section's whole promise is that its code works.");

        var cursor = heading + 1;
        for (var skipped = 0; skipped < index; skipped++)
        {
            var skipStart = Array.FindIndex(lines, cursor, line => line.StartsWith("```csharp", StringComparison.Ordinal));
            Assert.True(skipStart >= cursor,
                $"{AssetPath}'s '{Heading}' section has fewer than {index + 1} ```csharp blocks.");
            cursor = Array.FindIndex(lines, skipStart + 1, line => line.StartsWith("```", StringComparison.Ordinal)) + 1;
        }

        var start = Array.FindIndex(lines, cursor, line => line.StartsWith("```csharp", StringComparison.Ordinal));
        Assert.True(start >= cursor,
            $"{AssetPath}'s '{Heading}' section has fewer than {index + 1} ```csharp blocks.");

        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("```", StringComparison.Ordinal));
        Assert.True(end > start, $"{AssetPath}: a ```csharp fence under '{Heading}' is never closed.");

        return string.Join(Environment.NewLine, lines[(start + 1)..end]);
    }

    private static string WorkedExample() => CodeBlock(0);

    private static CSharpCompilation Compile() =>
        CSharpCompilation.Create(
            assemblyName: "AgentAssetWorkedExample",
            syntaxTrees: [CSharpSyntaxTree.ParseText(WorkedExample())],
            references: AnalyzerTestRunner.MetadataReferences,
            // A library, so the block's `Main` compiles as an ordinary method and the block needs no
            // adaptation — same reasoning as Readme_Should.
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// The worked example <b>and</b> the xUnit contract test beneath it, compiled together — which
    /// is how a reader meets them, since the test class names <c>IGreeter</c> from the block above.
    /// </summary>
    /// <remarks>
    /// This is the "and is contract-tested" third of the section's claim, and it needed no new
    /// reference set after all: <see cref="AnalyzerTestRunner"/>'s references are the trusted
    /// platform assemblies <i>plus every assembly loaded in the test host</i>, and xUnit is loaded
    /// in an xUnit host by definition. The queue note that predicted a fiddly xUnit reference set
    /// was wrong, and cheaply so — trying it was faster than reasoning about it.
    /// <para>
    /// Compiled but not executed. Running it would need a Portico application built from the block's
    /// own types, which <c>CliContractValidator&lt;IGreeter&gt;</c> does for itself — but the point
    /// here is the promise the document makes about its <i>code</i>, and a test class that does not
    /// compile cannot have contract-tested anything.
    /// </para>
    /// </remarks>
    [Fact]
    public void Compile_The_Contract_Test_Beside_It()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "AgentAssetContractTest",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(WorkedExample()),
                CSharpSyntaxTree.ParseText(CodeBlock(1)),
            ],
            references: AnalyzerTestRunner.MetadataReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"{AssetPath}'s contract-test block does not compile against its own worked example, " +
            $"though the section says the code is contract-tested:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(e =>
                $"  {e.Id} @ {e.Location.GetLineSpan().StartLinePosition}: {e.GetMessage()}")));
    }

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

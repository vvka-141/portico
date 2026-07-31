using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Portico.Analyzers;
using Portico.Testing;
using Xunit;

namespace Portico;

/// <summary>
/// The README's first code block is the most-read code in the project — it is the GitHub landing
/// page, and the charter names it as a 1.0 success metric ("tells the whole story"). POR-155 made
/// the sample in <c>src/Portico/PACKAGE-README.md</c> a compiled project, because a capability named
/// on a NuGet page is a promise. The block on the repository's own front page was left out of that,
/// and it is the one more people read.
/// </summary>
/// <remarks>
/// A framework whose pitch is "your examples cannot lie about what the CLI accepts" cannot ship a
/// landing-page example that does not compile. So the block is held to the stronger of the two
/// available standards: it must compile clean, and it must satisfy Portico's own analyzers — the
/// same POR001-POR013 a user gets the moment they install the package.
/// </remarks>
public sealed class Readme_Should
{
    private const string ReadmePath = "README.md";

    /// <summary>
    /// The first fenced <c>csharp</c> block in the README, verbatim. Extracted rather than copied
    /// into this file: a copy is a second source of truth, and it would keep passing while the
    /// README rotted beside it — the precise failure this guards against.
    /// </summary>
    private static string FirstCodeBlock()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root(), ReadmePath));

        var start = Array.FindIndex(lines, line => line.StartsWith("```csharp", StringComparison.Ordinal));
        Assert.True(start >= 0, $"{ReadmePath} has no ```csharp block. If the quickstart moved to " +
                                "another language or format, update this test — do not delete the guard.");

        var end = Array.FindIndex(lines, start + 1, line => line.StartsWith("```", StringComparison.Ordinal));
        Assert.True(end > start, $"{ReadmePath}'s first ```csharp fence is never closed.");

        return string.Join(Environment.NewLine, lines[(start + 1)..end]);
    }

    private static CSharpCompilation Compile() =>
        CSharpCompilation.Create(
            assemblyName: "ReadmeQuickstart",
            syntaxTrees: [CSharpSyntaxTree.ParseText(FirstCodeBlock())],
            references: AnalyzerTestRunner.MetadataReferences,
            // A library, so the block's `Main` is compiled as an ordinary method and the block needs
            // no adaptation to be tested. Adapting it would reintroduce the copy this avoids.
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Fact]
    public void Compile_Its_First_Code_Block()
    {
        var errors = Compile()
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"{ReadmePath}'s first code block does not compile:{Environment.NewLine}" +
            string.Join(Environment.NewLine, errors.Select(e => $"  {e.Id} @ {e.Location.GetLineSpan().StartLinePosition}: {e.GetMessage()}")));
    }

    /// <summary>
    /// Every analyzer the package ships, run over the block a new user copies first. A landing-page
    /// example that trips POR004 or POR012 would hand its reader a warning on their first build —
    /// and <c>TreatWarningsAsErrors</c> is on in the scaffolded template, so for them it is an error.
    /// </summary>
    [Fact]
    public async Task Satisfy_Every_Analyzer_It_Ships()
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
            $"{ReadmePath}'s first code block trips Portico's own analyzers:{Environment.NewLine}" +
            string.Join(Environment.NewLine, diagnostics.Select(d => $"  {d.Id}: {d.GetMessage()}")));
    }

    /// <summary>
    /// The <c>--help</c> transcript in the README is the output the README's own contract produces —
    /// compiled, loaded, and run, then compared.
    /// </summary>
    /// <remarks>
    /// POR-110 put that transcript in the first screenful because it is the cheapest, most convincing
    /// artefact available. It is also the single worst thing in the repository to let rot: a
    /// hand-written help transcript on the landing page of a framework whose entire pitch is <i>"your
    /// examples cannot lie about what the CLI accepts"</i> would be the exact drift it sells against,
    /// in the one place every visitor looks. The ticket says so in its own acceptance criteria.
    /// <para>
    /// So it is not proofread, it is executed. The block is emitted to an assembly, the tool type is
    /// instantiated out of it, and the application is run through <c>CliTestHarness</c> — the same
    /// pipeline a user gets. Renaming the option, dropping the description, or editing the fence by
    /// hand fails here.
    /// </para>
    /// </remarks>
    [Fact]
    public void Show_The_Help_Its_Own_Contract_Produces()
    {
        var transcript = HelpTranscript();

        using var stream = new MemoryStream();
        var emit = Compile().Emit(stream);
        Assert.True(emit.Success, "The README's first code block did not emit; see Compile_Its_First_Code_Block.");

        var toolType = Assembly.Load(stream.ToArray()).GetTypes().Single(type => type.Name == "AdminTool");
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(Activator.CreateInstance(toolType)!))
            .Run("admin --help");

        Assert.Equal(0, result.ExitCode);

        var actual = Normalize(result.StandardOut);
        var documented = Normalize(transcript);

        Assert.True(
            actual == documented,
            $"{ReadmePath}'s --help transcript is not what its contract produces." +
            $"{Environment.NewLine}--- README says ---{Environment.NewLine}{documented}" +
            $"{Environment.NewLine}--- the CLI prints ---{Environment.NewLine}{actual}" +
            $"{Environment.NewLine}Paste the real output. Do not edit the fence by hand.");
    }

    /// <summary>
    /// The fenced block that follows the <c>$ admin --help</c> prompt line, without that line.
    /// </summary>
    private static string HelpTranscript()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root(), ReadmePath));

        var prompt = Array.FindIndex(lines, line => line.StartsWith("$ admin --help", StringComparison.Ordinal));
        Assert.True(prompt > 0,
            $"{ReadmePath} no longer contains a '$ admin --help' transcript. It is an acceptance " +
            "criterion of POR-110 that one appears in the first screenful — restore it, or update " +
            "this test deliberately rather than deleting the guard.");

        var end = Array.FindIndex(lines, prompt + 1, line => line.StartsWith("```", StringComparison.Ordinal));
        Assert.True(end > prompt, $"{ReadmePath}'s --help transcript fence is never closed.");

        return string.Join(Environment.NewLine, lines[(prompt + 1)..end]);
    }

    /// <summary>Trailing whitespace and blank-line padding are not the contract; the text is.</summary>
    private static string Normalize(string text) =>
        string.Join(
            Environment.NewLine,
            text.ReplaceLineEndings(Environment.NewLine)
                .Split(Environment.NewLine)
                .Select(line => line.TrimEnd())
                .SkipWhile(string.IsNullOrEmpty)
                .Reverse()
                .SkipWhile(string.IsNullOrEmpty)
                .Reverse());
}

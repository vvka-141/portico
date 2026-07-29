using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// Minimal analyzer test driver. Compiles a snippet against the Portico.Core assembly,
/// runs the analyzer, and returns the diagnostics it produced. Avoids the community
/// <c>Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit</c> package because it pulls an
/// ancient Roslyn (1.0.0) that conflicts with the analyzer's modern 4.x reference.
/// </summary>
internal static class AnalyzerTestRunner
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    /// <summary>The metadata reference set used to compile analyzer/code-fix test snippets.</summary>
    internal static ImmutableArray<MetadataReference> MetadataReferences => References;

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // The trusted-platform-assemblies list: every assembly the host resolves against, whether or
        // not it has been loaded yet. That "whether or not" is the whole point — this used to walk
        // AppDomain.CurrentDomain.GetAssemblies() and call the result "correct and deterministic
        // across machines", and it was neither. A snippet could only reference assemblies the test
        // host happened to have loaded already, so System.Console did not bind: nothing in the test
        // process had touched Console, so the assembly was absent and `Console.WriteLine(...)`
        // failed to compile with CS1069.
        //
        // That is a quiet failure mode rather than a loud one. A test asserting a diagnostic IS
        // produced still fails visibly if the type does not bind; a test asserting a snippet is
        // CLEAN passes either way, and would keep passing if the analyzer stopped firing entirely.
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            ?? [];

        // Loaded assemblies still go in: anything resolved outside the TPA set (a test-host plugin
        // path) would otherwise be lost. Deduplicated by file name — the same assembly reachable by
        // two paths is an ambiguous-reference compile error, not a fatter reference set.
        var located = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => assembly.Location);

        return paths
            .Concat(located)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => (MetadataReference)MetadataReference.CreateFromFile(group.First()))
            .ToImmutableArray();
    }

    /// <summary>Runs <paramref name="analyzer"/> over <paramref name="source"/> and returns the diagnostics.</summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
        DiagnosticAnalyzer analyzer,
        string source,
        CancellationToken cancellationToken = default)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName: "AnalyzerTest",
            syntaxTrees: new[] { syntaxTree },
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer),
            new CompilationWithAnalyzersOptions(
                options: null!,
                onAnalyzerException: null!,
                concurrentAnalysis: false,
                logAnalyzerExecutionTime: false));

        var all = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        return all;
    }

    /// <summary>Shortcut for the common assertion: no diagnostics at all.</summary>
    public static async Task AssertCleanAsync(DiagnosticAnalyzer analyzer, string source)
    {
        var diags = await RunAsync(analyzer, source).ConfigureAwait(false);
        if (diags.IsEmpty) return;
        var report = string.Join(Environment.NewLine,
            diags.Select(d => $"  {d.Id} @ {d.Location}: {d.GetMessage()}"));
        throw new Xunit.Sdk.XunitException(
            $"Expected no diagnostics but got {diags.Length}:{Environment.NewLine}{report}");
    }
}

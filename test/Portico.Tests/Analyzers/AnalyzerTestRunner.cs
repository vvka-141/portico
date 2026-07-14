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
        // Reference every loaded assembly that has a physical location — covers BCL, System.*,
        // and the Portico.Core test-hosted assembly. A fatter reference set than necessary,
        // but correct and deterministic across machines.
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
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

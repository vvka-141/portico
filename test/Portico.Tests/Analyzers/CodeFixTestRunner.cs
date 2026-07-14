using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Portico.Analyzers;

/// <summary>
/// Hand-rolled CodeFix apply-and-verify driver. Compiles a snippet, runs the analyzer to produce
/// the diagnostic, applies the first action the code-fix registers for it, formats the result, and
/// returns the fixed source. Reuses <see cref="AnalyzerTestRunner"/>'s reference set so the same
/// BCL + Portico.Core surface is in scope. Shared by the POR004/POR006 fixes (SOL-15) and reusable
/// by the broader CodeFix harness work (SOL-37).
/// </summary>
internal static class CodeFixTestRunner
{
    /// <summary>Applies the matching fix and returns the resulting source text.</summary>
    public static async Task<string> ApplyAsync(
        DiagnosticAnalyzer analyzer,
        CodeFixProvider codeFix,
        string source,
        CancellationToken cancellationToken = default)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "CodeFixTest", "CodeFixTest", LanguageNames.CSharp)
            .AddMetadataReferences(projectId, AnalyzerTestRunner.MetadataReferences)
            .WithProjectCompilationOptions(
                projectId,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddDocument(documentId, "Test.cs", SourceText.From(source));

        var document = solution.GetDocument(documentId)!;

        var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        var withAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create(analyzer));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);

        var diagnostic = diagnostics.FirstOrDefault(d => codeFix.FixableDiagnosticIds.Contains(d.Id))
            ?? throw new Xunit.Sdk.XunitException(
                $"Analyzer produced no diagnostic fixable by {codeFix.GetType().Name} " +
                $"(expected one of: {string.Join(", ", codeFix.FixableDiagnosticIds)}).");

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            cancellationToken);
        await codeFix.RegisterCodeFixesAsync(context).ConfigureAwait(false);

        if (actions.Count == 0)
        {
            throw new Xunit.Sdk.XunitException($"{codeFix.GetType().Name} registered no code actions.");
        }

        var operations = await actions[0].GetOperationsAsync(cancellationToken).ConfigureAwait(false);
        var applied = operations.OfType<ApplyChangesOperation>().Single();
        var changedDocument = applied.ChangedSolution.GetDocument(documentId)!;

        var formatted = await Formatter.FormatAsync(changedDocument, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = await formatted.GetTextAsync(cancellationToken).ConfigureAwait(false);
        return text.ToString();
    }

    /// <summary>Asserts <paramref name="source"/> compiles with no error-severity diagnostics.</summary>
    public static void AssertCompiles(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "CodeFixCompileCheck",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: AnalyzerTestRunner.MetadataReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (errors.Length == 0) return;

        var report = string.Join("\n", errors.Select(d => $"  {d.Id}: {d.GetMessage()}"));
        throw new Xunit.Sdk.XunitException($"Fixed source did not compile:\n{report}\n\n--- source ---\n{source}");
    }
}

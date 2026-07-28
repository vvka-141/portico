using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR011 — a <c>[CliRoute]</c> string repeats the same <c>{placeholder}</c> name. At dispatch
/// the second slot overwrites the first — silent data loss that <c>CliContractValidator</c> does
/// not catch. The runtime guard at <c>CliApplication.Create</c> is the backstop for builds without
/// the analyzer.
/// </summary>
/// <remarks>
/// Checks the method-level route only. A type-level <c>[CliRoute]</c> prefix is concatenated with
/// the method-level route at runtime, so a placeholder repeated across both levels is caught by the
/// runtime guard but not by this analyzer — the analyzer cannot see the concatenated shape. This is
/// the same conservative stance POR001/POR005 take: stay silent rather than risk a false positive.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateRoutePlaceholderAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.DuplicateRoutePlaceholder);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.AttributeLists.Count == 0) return;

        var literal = CliRouteFacts.TryGetRouteLiteral(context, method);
        if (literal is null) return;

        var route = literal.Token.ValueText;
        var seen = new HashSet<string>();

        foreach (var placeholderName in CliRouteFacts.Placeholders(route))
        {
            if (seen.Add(placeholderName)) continue;

            context.ReportDiagnostic(Diagnostic.Create(
                PorticoDiagnostics.DuplicateRoutePlaceholder,
                literal.GetLocation(),
                route,
                method.Identifier.ValueText,
                placeholderName));
        }
    }
}

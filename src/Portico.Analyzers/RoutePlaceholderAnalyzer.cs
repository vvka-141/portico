using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR001 — reports route placeholders (<c>{name}</c> inside a <c>[CliRoute]</c> string) that
/// don't match any parameter on the decorated method.
/// </summary>
/// <remarks>
/// Uses a purely syntactic check against parameter names (plus a semantic check to confirm the
/// attribute is Portico's <c>CliRouteAttribute</c>, not some user-defined lookalike). Tokenization
/// lives in <see cref="CliRouteFacts"/>, shared with POR005 — the two rules ask the same question
/// from opposite sides and must agree about what a placeholder is.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RoutePlaceholderAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.RoutePlaceholderMismatch);

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

        var parameterNames = method.ParameterList.Parameters
            .Select(p => p.Identifier.ValueText)
            .ToImmutableHashSet();

        foreach (var placeholderName in CliRouteFacts.Placeholders(literal.Token.ValueText))
        {
            if (parameterNames.Contains(placeholderName)) continue;

            var available = parameterNames.Count == 0
                ? "(method takes no parameters)"
                : string.Join(", ", parameterNames.OrderBy(n => n));

            context.ReportDiagnostic(Diagnostic.Create(
                PorticoDiagnostics.RoutePlaceholderMismatch,
                literal.GetLocation(),
                placeholderName,
                method.Identifier.ValueText,
                available));
        }
    }
}

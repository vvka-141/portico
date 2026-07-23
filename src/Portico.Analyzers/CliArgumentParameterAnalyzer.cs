using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR005 — reports a <c>[CliArgument]</c> on a parameter the method's <c>[CliRoute]</c> declares no
/// <c>{placeholder}</c> for. The mirror image of POR001, which reports a placeholder with no
/// parameter; together they pin the rule that a command's path is declared entirely by its route
/// string.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CliArgumentParameterAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.CliArgumentParameterMismatch);

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

        // No literal route to compare against — a non-CLI method, or a route the analyzer cannot
        // evaluate. Stay silent and let the runtime check decide; an analyzer must never fail a
        // build that works.
        var literal = CliRouteFacts.TryGetRouteLiteral(context, method);
        if (literal is null) return;

        var route = literal.Token.ValueText;
        var placeholders = CliRouteFacts.Placeholders(route).ToImmutableHashSet();

        foreach (var parameter in method.ParameterList.Parameters)
        {
            var parameterName = parameter.Identifier.ValueText;
            if (placeholders.Contains(parameterName)) continue;

            foreach (var attribute in parameter.AttributeLists.SelectMany(al => al.Attributes))
            {
                if (!CliArgumentAttributeFacts.IsCliArgument(context, attribute)) continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    PorticoDiagnostics.CliArgumentParameterMismatch,
                    attribute.GetLocation(),
                    method.Identifier.ValueText,
                    parameterName,
                    route,
                    $"{route} {{{parameterName}}}".Trim()));
                break;
            }
        }
    }
}

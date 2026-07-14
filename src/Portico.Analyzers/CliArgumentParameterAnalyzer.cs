using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR005 — reports a method-level <c>[CliArgument(parameterName, description)]</c> whose
/// <c>parameterName</c> does not match any parameter on the decorated method (usually a stale
/// <c>nameof(...)</c> after a rename). The <c>[CliArgument]</c> analogue of POR001.
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

        var parameterNames = method.ParameterList.Parameters
            .Select(p => p.Identifier.ValueText)
            .ToImmutableHashSet();

        foreach (var attribute in method.AttributeLists.SelectMany(al => al.Attributes))
        {
            if (!CliArgumentAttributeFacts.TryGetMethodLevelParameterName(
                    context, attribute, out var parameterNameExpression))
            {
                continue;
            }

            var constant = context.SemanticModel.GetConstantValue(parameterNameExpression, context.CancellationToken);
            if (!constant.HasValue || constant.Value is not string target)
            {
                // Non-constant parameter name — cannot resolve, leave it to the runtime check.
                continue;
            }

            if (parameterNames.Contains(target)) continue;

            var available = parameterNames.Count == 0
                ? "(method takes no parameters)"
                : string.Join(", ", parameterNames.OrderBy(n => n));

            context.ReportDiagnostic(Diagnostic.Create(
                PorticoDiagnostics.CliArgumentParameterMismatch,
                parameterNameExpression.GetLocation(),
                method.Identifier.ValueText,
                target,
                available));
        }
    }
}

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR004 — warns when a method decorated with <c>[CliRoute]</c> has no
/// <c>[CliCommandExample]</c>. The framework's signature feature per CHARTER §4.2 is
/// "examples are tests" — a route without an example isn't tested and isn't self-documenting.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingCommandExampleAnalyzer : DiagnosticAnalyzer
{
    private const string CliRouteAttributeFullName = "Portico.CliRouteAttribute";
    private const string CliCommandExampleAttributeFullName = "Portico.CliCommandExampleAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.MissingCommandExample);

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

        var hasRoute = false;
        var hasExample = false;
        AttributeSyntax? routeAttribute = null;

        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var symbol = context.SemanticModel
                    .GetSymbolInfo(attr, context.CancellationToken).Symbol;
                var fullName = symbol?.ContainingType?.ToDisplayString();
                if (fullName is null) continue;

                switch (fullName)
                {
                    case CliRouteAttributeFullName:
                        hasRoute = true;
                        routeAttribute ??= attr;
                        break;
                    case CliCommandExampleAttributeFullName:
                        hasExample = true;
                        break;
                }

                if (hasExample) break;
            }
            if (hasExample) break;
        }

        if (!hasRoute || hasExample || routeAttribute is null) return;

        context.ReportDiagnostic(Diagnostic.Create(
            PorticoDiagnostics.MissingCommandExample,
            routeAttribute.GetLocation(),
            method.Identifier.ValueText));
    }
}

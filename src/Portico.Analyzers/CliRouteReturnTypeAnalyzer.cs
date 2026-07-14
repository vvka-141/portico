using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR008 — a <c>[CliRoute]</c> method must return <c>int</c> or <c>Task&lt;int&gt;</c>.
/// Flags <c>void</c>, <c>async void</c>, non-generic <c>Task</c>, and any other return type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CliRouteReturnTypeAnalyzer : DiagnosticAnalyzer
{
    private const string CliRouteAttributeFullName = "Portico.CliRouteAttribute";
    private const string TaskOfIntFullName = "System.Threading.Tasks.Task<int>";
    private const string IntFullName = "int";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.InvalidCliRouteReturnType);

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
        if (!HasCliRoute(context, method)) return;

        var returnType = context.SemanticModel.GetTypeInfo(method.ReturnType, context.CancellationToken).Type;
        if (returnType is null) return;
        if (IsValidReturnType(returnType)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            PorticoDiagnostics.InvalidCliRouteReturnType,
            method.ReturnType.GetLocation(),
            method.Identifier.ValueText,
            returnType.ToDisplayString()));
    }

    private static bool HasCliRoute(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var symbol = context.SemanticModel
                    .GetSymbolInfo(attr, context.CancellationToken).Symbol;
                if (symbol?.ContainingType?.ToDisplayString() == CliRouteAttributeFullName)
                    return true;
            }
        }
        return false;
    }

    private static bool IsValidReturnType(ITypeSymbol returnType)
    {
        // int
        if (returnType.SpecialType == SpecialType.System_Int32) return true;

        // Task<int>
        if (returnType is INamedTypeSymbol named &&
            named.IsGenericType &&
            named.TypeArguments.Length == 1 &&
            named.TypeArguments[0].SpecialType == SpecialType.System_Int32 &&
            named.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<TResult>")
        {
            return true;
        }

        return false;
    }
}

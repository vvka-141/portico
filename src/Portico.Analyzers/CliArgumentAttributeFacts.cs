using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// Shared semantic predicates for <c>Portico.CliArgumentAttribute</c>, used by the
/// POR005 and POR007 analyzers so both recognise the attribute (and its method-level constructor)
/// identically.
/// </summary>
internal static class CliArgumentAttributeFacts
{
    private const string CliArgumentAttributeFullName = "Portico.CliArgumentAttribute";

    /// <summary>
    /// True if <paramref name="attribute"/> binds to <c>CliArgumentAttribute</c> (or a subclass).
    /// </summary>
    public static bool IsCliArgument(SyntaxNodeAnalysisContext context, AttributeSyntax attribute)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol;
        return DerivesFromCliArgument(symbol?.ContainingType);
    }

    /// <summary>
    /// True if <paramref name="attribute"/> uses the method-level
    /// <c>CliArgumentAttribute(string parameterName, string description)</c> constructor; on success
    /// <paramref name="parameterNameExpression"/> is the argument expression bound to
    /// <c>parameterName</c> (typically a <c>nameof(...)</c> or a string literal).
    /// </summary>
    public static bool TryGetMethodLevelParameterName(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attribute,
        out ExpressionSyntax parameterNameExpression)
    {
        parameterNameExpression = null!;

        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol
                is not IMethodSymbol ctor)
        {
            return false;
        }

        if (!DerivesFromCliArgument(ctor.ContainingType)) return false;

        // Method-level constructor: (string parameterName, string description).
        if (ctor.Parameters.Length != 2 || ctor.Parameters[0].Name != "parameterName")
        {
            return false;
        }

        var arguments = attribute.ArgumentList?.Arguments;
        if (arguments is not { Count: >= 1 }) return false;

        parameterNameExpression = arguments.Value[0].Expression;
        return true;
    }

    private static bool DerivesFromCliArgument(INamedTypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == CliArgumentAttributeFullName) return true;
        }
        return false;
    }
}

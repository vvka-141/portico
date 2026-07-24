using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// Shared semantic predicate for <c>Portico.CliArgumentAttribute</c>, used by the POR005
/// analyzer so it recognises the attribute (and its subclasses) identically to the runtime.
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

    private static bool DerivesFromCliArgument(INamedTypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == CliArgumentAttributeFullName) return true;
        }
        return false;
    }
}

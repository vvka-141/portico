using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR003 — flags <c>[CliOption]</c> attributes whose first-position
/// string argument isn't a valid pipe-separated alias list. Valid forms:
/// <c>"--verbose"</c>, <c>"--verbose|-v"</c>, <c>"-t"</c>. Invalid forms: empty strings,
/// whitespace, names without dashes, tokens that are nothing but dashes, trailing / leading
/// pipes, embedded whitespace inside an alias.
/// </summary>
/// <remarks>
/// Only <see cref="CliOptionAttribute"/> is validated — <see cref="CliArgumentAttribute"/>'s
/// first argument is a parameter name or description, not an alias list. (Subclasses of
/// <c>CliOptionAttribute</c> aren't recognized; the runtime throws on malformed aliases either
/// way.)
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CliOptionSpecAnalyzer : DiagnosticAnalyzer
{
    private const string CliOptionAttributeFullName = "Portico.CliOptionAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.MalformedCliOptionSpec);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attr = (AttributeSyntax)context.Node;
        var symbol = context.SemanticModel.GetSymbolInfo(attr, context.CancellationToken).Symbol;
        if (symbol?.ContainingType?.ToDisplayString() != CliOptionAttributeFullName) return;

        var firstArg = attr.ArgumentList?.Arguments.FirstOrDefault();
        if (firstArg?.Expression is not LiteralExpressionSyntax literal) return;
        if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) return;

        var spec = literal.Token.ValueText;
        var problem = ValidateSpec(spec);
        if (problem is null) return;

        context.ReportDiagnostic(Diagnostic.Create(
            PorticoDiagnostics.MalformedCliOptionSpec,
            literal.GetLocation(),
            spec,
            problem));
    }

    /// <summary>
    /// Returns a human-readable reason string when the spec is invalid, or <c>null</c> when the
    /// spec is well-formed. Same rules as <c>CliOptionAttribute</c>'s runtime parser.
    /// </summary>
    internal static string? ValidateSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return "empty or whitespace-only";

        var parts = spec.Split('|');
        if (parts.Length == 0)
            return "no aliases found";

        foreach (var raw in parts)
        {
            var alias = raw.Trim();
            if (alias.Length == 0)
                return "contains an empty alias (look for adjacent or trailing '|')";
            if (raw != alias)
                return $"alias '{raw}' has surrounding whitespace";
            if (alias[0] != '-')
                return $"alias '{alias}' is missing a leading '-' (use '-x' for short, '--name' for long)";
            if (alias.Length == 1)
                return $"alias '{alias}' is just a dash with no name";
            if (alias == "--")
                return "alias '--' is reserved as the POSIX end-of-options terminator";
            // Long form: --name where name is at least one char.
            // Short form: -x where x is one char (allow -name multi-char shorts intentionally).
            foreach (var ch in alias)
            {
                if (char.IsWhiteSpace(ch))
                    return $"alias '{alias}' contains whitespace";
            }
        }

        return null;
    }
}

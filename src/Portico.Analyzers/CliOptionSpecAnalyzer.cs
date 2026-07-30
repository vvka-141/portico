using System.Collections.Generic;
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

        // The repaired spec travels as a diagnostic property, not as text in the message. The code fix
        // registers itself only when this is present, so the decision "is this mechanically
        // correctable" is made HERE, beside ValidateSpec, and cannot drift from it. A fix that parsed
        // its own diagnostic message would break on the next rewording — and rewording is what
        // docs/explanation/analyzer-message-audit.md exists to encourage (POR-122).
        var properties = ImmutableDictionary<string, string?>.Empty;
        if (TryRepairSpec(spec) is { } repaired)
        {
            properties = properties.Add(RepairedSpecProperty, repaired);
        }

        context.ReportDiagnostic(Diagnostic.Create(
            PorticoDiagnostics.MalformedCliOptionSpec,
            literal.GetLocation(),
            properties,
            spec,
            problem));
    }

    /// <summary>The diagnostic-property key carrying a mechanically repaired spec, when one exists.</summary>
    internal const string RepairedSpecProperty = "RepairedSpec";

    /// <summary>
    /// A corrected spec for the failure modes that carry unambiguous author intent, or
    /// <see langword="null"/> when there is nothing to infer.
    /// </summary>
    /// <remarks>
    /// Two modes are repairable, and only two:
    /// <list type="bullet">
    ///   <item><description>an undashed alias — <c>"verbose"</c> means <c>"--verbose"</c>, and a
    ///     single character means <c>"-v"</c>;</description></item>
    ///   <item><description>an empty segment from a leading, doubled or trailing pipe —
    ///     <c>"--verbose|"</c> means <c>"--verbose"</c>.</description></item>
    /// </list>
    /// Everything else returns <see langword="null"/> deliberately. An empty spec, a whitespace-only
    /// one, and a bare <c>"-"</c> or <c>"--"</c> carry no name to recover, and an alias padded with
    /// whitespace is left alone rather than silently reshaped. <b>A code fix that guesses is worse than
    /// no code fix, because the user accepts it without reading.</b>
    /// <para>
    /// The result is re-validated before it is offered, so this can never hand the user a spec the
    /// analyzer would report again.
    /// </para>
    /// </remarks>
    internal static string? TryRepairSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;

        var repaired = new List<string>();
        foreach (var raw in spec.Split('|'))
        {
            // An empty (or whitespace-only) segment is the trailing/doubled-pipe case: drop it.
            if (raw.Trim().Length == 0) continue;

            // A padded alias is not this fix's business — reshaping whitespace inside a spec is a
            // judgement about layout, not a correction.
            if (raw != raw.Trim()) return null;

            var alias = raw;
            if (alias.Any(char.IsWhiteSpace)) return null;

            // Dashes with no name behind them: nothing to recover.
            if (alias == "-" || alias == "--") return null;

            if (alias[0] != '-')
            {
                alias = alias.Length == 1 ? "-" + alias : "--" + alias;
            }

            repaired.Add(alias);
        }

        if (repaired.Count == 0) return null;

        var result = string.Join("|", repaired);

        // Never offer something still invalid, and never offer a no-op.
        return result != spec && ValidateSpec(result) is null ? result : null;
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

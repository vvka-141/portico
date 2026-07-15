using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
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
/// attribute is Portico' <c>CliRouteAttribute</c>, not some user-defined lookalike). Does not
/// currently recognise subclasses of <c>CliRouteAttribute</c> — the runtime check at
/// <c>CliApplication.Create</c> time fires as a safety net for those.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RoutePlaceholderAnalyzer : DiagnosticAnalyzer
{
    private const string CliRouteAttributeFullName = "Portico.CliRouteAttribute";

    // Mirrors the runtime's CliMethodInfo.PlaceholderRegex EXACTLY: anchored (^...$), matched
    // against a single whitespace-split token — NOT run unanchored over the whole route string.
    // The runtime treats "{id}" as a placeholder only when it is the entire token, so "user{id}"
    // is a literal it routes fine; an unanchored scan here reported POR001 on that and failed a
    // working build (POR-61). The analyzer cannot reference the framework (netstandard2.0 → net10.0),
    // so the algorithm is reproduced, not shared — keep both in lock-step by hand.
    private static readonly Regex PlaceholderRegex = new(
        @"^\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RouteTokenSeparator = new(@"\s+", RegexOptions.Compiled);

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

        // Collect parameter names once per method.
        var parameterNames = method.ParameterList.Parameters
            .Select(p => p.Identifier.ValueText)
            .ToImmutableHashSet();

        foreach (var attributeList in method.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (!IsCliRouteAttribute(context, attribute)) continue;

                var firstArg = attribute.ArgumentList?.Arguments.FirstOrDefault();
                if (firstArg?.Expression is not LiteralExpressionSyntax literal) continue;
                if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) continue;

                var routeString = literal.Token.ValueText;

                // Tokenize-then-anchor-match, exactly as CliMethodInfo.ExtractRouteParts does at
                // runtime: split on whitespace, trim, and treat a token as a placeholder only when
                // it is ENTIRELY {name}.
                foreach (var token in RouteTokenSeparator.Split(routeString))
                {
                    if (string.IsNullOrWhiteSpace(token)) continue;
                    var match = PlaceholderRegex.Match(token.Trim());
                    if (!match.Success) continue;

                    var placeholderName = match.Groups["name"].Value;
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
    }

    private static bool IsCliRouteAttribute(SyntaxNodeAnalysisContext context, AttributeSyntax attribute)
    {
        var symbol = context.SemanticModel
            .GetSymbolInfo(attribute, context.CancellationToken)
            .Symbol;
        var containing = symbol?.ContainingType;
        return containing is not null &&
               containing.ToDisplayString() == CliRouteAttributeFullName;
    }
}

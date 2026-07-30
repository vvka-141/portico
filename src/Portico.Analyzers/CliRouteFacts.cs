using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// Shared route-string facts for the analyzers that reason about <c>[CliRoute]</c> — POR001 (a
/// placeholder with no parameter) and POR005 (a <c>[CliArgument]</c> with no placeholder). Both ask
/// the same question from opposite sides, so they must tokenize identically or they will disagree
/// about what a placeholder is.
/// </summary>
internal static class CliRouteFacts
{
    private const string CliRouteAttributeFullName = "Portico.CliRouteAttribute";

    // Mirrors the runtime's CliMethodInfo.PlaceholderRegex EXACTLY: anchored (^...$), matched
    // against a single whitespace-split token — NOT run unanchored over the whole route string.
    // The runtime treats "{id}" as a placeholder only when it is the entire token, so "user{id}"
    // is a literal it routes fine; an unanchored scan reported POR001 on that and failed a working
    // build (POR-61). The analyzer cannot reference the framework (netstandard2.0 → net10.0), so
    // the algorithm is reproduced, not shared — keep both in lock-step by hand.
    private static readonly Regex PlaceholderRegex = new(
        @"^\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // The separator is CAPTURED so Split returns the whitespace runs alongside the tokens. Placeholders
    // skips them; RenamePlaceholder puts them back verbatim. One tokenizer with two consumers is the
    // point: the rename originally split on a single space of its own, so a tab-separated route — which
    // the runtime and Placeholders both segment fine — reported POR001 and then renamed nothing.
    private static readonly Regex RouteTokenSeparator = new(@"(\s+)", RegexOptions.Compiled);

    /// <summary>
    /// True if <paramref name="attribute"/> binds to <c>Portico.CliRouteAttribute</c>. Subclasses are
    /// not recognised (the type is sealed); the runtime check at <c>CliApplication.Create</c> is the
    /// safety net if that ever changes.
    /// </summary>
    public static bool IsCliRoute(SyntaxNodeAnalysisContext context, AttributeSyntax attribute)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol;
        return symbol?.ContainingType?.ToDisplayString() == CliRouteAttributeFullName;
    }

    /// <summary>
    /// The method's <c>[CliRoute]</c> string literal, or <see langword="null"/> when the method has
    /// no route or its route is not a compile-time literal (an interpolation or a const reference —
    /// left to the runtime check rather than guessed at).
    /// </summary>
    public static LiteralExpressionSyntax? TryGetRouteLiteral(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax method)
    {
        foreach (var attribute in method.AttributeLists.SelectMany(al => al.Attributes))
        {
            if (!IsCliRoute(context, attribute)) continue;

            var firstArg = attribute.ArgumentList?.Arguments.FirstOrDefault();
            if (firstArg?.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal;
            }
        }
        return null;
    }

    /// <summary>
    /// The placeholder names in a route string, in route order. Tokenize-then-anchor-match, exactly
    /// as <c>CliMethodInfo.ExtractRouteParts</c> does at runtime.
    /// </summary>
    public static IEnumerable<string> Placeholders(string routeString)
    {
        foreach (var token in RouteTokenSeparator.Split(routeString))
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            var match = PlaceholderRegex.Match(token.Trim());
            if (match.Success) yield return match.Groups["name"].Value;
        }
    }

    /// <summary>
    /// <paramref name="routeString"/> with the whole-token placeholder <c>{from}</c> rewritten to
    /// <c>{to}</c>. Every other token is left byte-for-byte alone, including a braced <i>literal</i>
    /// such as <c>user{from}</c>, and every whitespace run is preserved.
    /// </summary>
    /// <remarks>
    /// This lives beside <see cref="Placeholders"/>, and not in the code fix that calls it, because the
    /// two have to agree about what a token is: a rename that finds nothing where the rule reported
    /// something is a fix that silently does nothing, which is worse for the author than an error with
    /// no fix at all. Sharing the tokenizer is what makes that disagreement unrepresentable rather than
    /// merely unlikely (POR-122).
    /// <para>
    /// Whitespace is restored verbatim rather than normalised to single spaces: reformatting the
    /// author's route is not part of correcting one placeholder in it.
    /// </para>
    /// </remarks>
    public static string RenamePlaceholder(string routeString, string from, string to)
    {
        var parts = RouteTokenSeparator.Split(routeString);
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "{" + from + "}") parts[i] = "{" + to + "}";
        }

        return string.Concat(parts);
    }
}

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Portico.Analyzers;

/// <summary>
/// Code-fix for <c>POR003</c> — repairs a malformed <c>[CliOption]</c> spec, but <b>only</b> for the
/// failure modes that carry unambiguous author intent.
/// </summary>
/// <remarks>
/// Two modes are repaired: an undashed alias (<c>"verbose"</c> → <c>"--verbose"</c>, or <c>"-v"</c> for a
/// single character) and an empty segment from a leading, doubled or trailing pipe (<c>"--verbose|"</c> →
/// <c>"--verbose"</c>).
/// <para>
/// <b>Nothing else is offered, on purpose.</b> An empty spec, a whitespace-only one, and a bare <c>"-"</c>
/// or <c>"--"</c> have no name to recover from. A code fix that guesses there is worse than no code fix,
/// because the user accepts it without reading — so no action is registered at all and the error stands
/// with its message.
/// </para>
/// <para>
/// This provider deliberately contains <b>no repair logic</b>. Whether a spec is repairable, and into
/// what, is decided in <see cref="CliOptionSpecAnalyzer.TryRepairSpec"/> — beside the validator whose
/// rules it has to agree with — and travels here as a diagnostic property. Two copies of that judgement
/// would drift, and the one in the fix provider would be the copy nobody notices is wrong.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CliOptionSpecCodeFix))]
[Shared]
public sealed class CliOptionSpecCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(PorticoDiagnostics.MalformedCliOptionSpec.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];

        // No repaired spec means the analyzer found nothing to infer. Registering nothing is the fix.
        if (!diagnostic.Properties.TryGetValue(CliOptionSpecAnalyzer.RepairedSpecProperty, out var repaired) ||
            string.IsNullOrEmpty(repaired))
        {
            return;
        }

        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        // FindToken(...).Parent, not FindNode(...): the diagnostic's span IS the literal's span, and
        // FindNode ties in favour of the enclosing AttributeArgument — after which AncestorsAndSelf walks
        // upward and never sees the literal at all. The token's parent is the literal, unambiguously.
        var literal = root
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault();
        if (literal is null || !literal.IsKind(SyntaxKind.StringLiteralExpression)) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Change the spec to \"{repaired}\"",
                createChangedDocument: ct => RewriteSpecAsync(context.Document, literal, repaired!, ct),
                // One key for the rule: a copy-pasted option declaration produces several identical
                // POR003s, and Fix-all should clear them together.
                equivalenceKey: nameof(CliOptionSpecCodeFix)),
            diagnostic);
    }

    private static async Task<Document> RewriteSpecAsync(
        Document document,
        LiteralExpressionSyntax literal,
        string repaired,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        return document.WithSyntaxRoot(root.ReplaceNode(
            literal,
            CliArgumentRouteCodeFix.StringLiteral(repaired).WithTriviaFrom(literal)));
    }
}

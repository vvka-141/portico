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
/// Code-fix for <c>POR005</c> — appends the missing <c>{placeholder}</c> to the method's
/// <c>[CliRoute]</c> string, so a <c>[CliArgument]</c> the route never mentioned gains a position to
/// bind to.
/// </summary>
/// <remarks>
/// The most deterministic of Portico's fixes, and the reason POR-122 ranks it first: there is exactly
/// one correct answer and the analyzer already computes it.
/// <para>
/// <b>The corrected route is recomputed here, not read back out of the diagnostic.</b> POR005's message
/// format ends with <c>[CliRoute("{3}")]</c> and <c>{3}</c> is the very string this fix produces — so
/// lifting it out of the formatted message would work today and break the first time the message is
/// reworded. Since <c>analyzer-message-audit.md</c> exists to encourage exactly that rewording, a fix
/// that depended on the wording would be a trap. The append rule is four tokens long; duplicating it is
/// cheaper than coupling to prose.
/// </para>
/// <para>
/// Appending, rather than inserting, is deliberate: the route string is the command's path in full, and
/// where in that path the argument belongs is the author's decision. Last position is the only one that
/// cannot silently change the meaning of an existing segment — and it is where a trailing argument
/// almost always goes.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CliArgumentRouteCodeFix))]
[Shared]
public sealed class CliArgumentRouteCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(PorticoDiagnostics.CliArgumentParameterMismatch.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];

        // The diagnostic points at the [CliArgument] attribute, so the parameter it decorates — and
        // therefore the placeholder name — is reachable syntactically. Nothing needs to be parsed out
        // of the message.
        var node = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        var parameter = node?.AncestorsAndSelf().OfType<ParameterSyntax>().FirstOrDefault();
        if (parameter is null) return;

        var method = parameter.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null) return;

        var routeLiteral = FindRouteLiteral(method);
        if (routeLiteral is null) return;

        var parameterName = parameter.Identifier.ValueText;
        var corrected = $"{routeLiteral.Token.ValueText} {{{parameterName}}}".Trim();

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add '{{{parameterName}}}' to the route",
                createChangedDocument: ct => RewriteRouteAsync(context.Document, routeLiteral, corrected, ct),
                // One key for the whole rule: a parameter rename produces several POR005s at once and
                // Fix-all has to sweep them in one pass. Keying per placeholder name would put each in
                // its own bucket and defeat that (POR-122).
                equivalenceKey: nameof(CliArgumentRouteCodeFix)),
            diagnostic);
    }

    /// <summary>
    /// The <c>[CliRoute]</c> string literal on the method, found syntactically.
    /// </summary>
    /// <remarks>
    /// Matched by attribute name rather than by symbol: a code fix runs on a document that may not have
    /// a usable semantic model, and the diagnostic being present already establishes that the analyzer
    /// resolved the attribute semantically. Both the <c>CliRoute</c> and <c>CliRouteAttribute</c>
    /// spellings are accepted, since C# permits either at the use site.
    /// </remarks>
    internal static LiteralExpressionSyntax? FindRouteLiteral(MethodDeclarationSyntax method)
    {
        foreach (var attribute in method.AttributeLists.SelectMany(list => list.Attributes))
        {
            var name = attribute.Name.ToString();
            var simple = name.Substring(name.LastIndexOf('.') + 1);
            if (simple != "CliRoute" && simple != "CliRouteAttribute") continue;

            if (attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal;
            }
        }

        return null;
    }

    private static async Task<Document> RewriteRouteAsync(
        Document document,
        LiteralExpressionSyntax routeLiteral,
        string corrected,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        return document.WithSyntaxRoot(
            root.ReplaceNode(routeLiteral, StringLiteral(corrected).WithTriviaFrom(routeLiteral)));
    }

    /// <summary>A verbatim-free string literal carrying <paramref name="value"/>.</summary>
    internal static LiteralExpressionSyntax StringLiteral(string value) =>
        SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(value));
}

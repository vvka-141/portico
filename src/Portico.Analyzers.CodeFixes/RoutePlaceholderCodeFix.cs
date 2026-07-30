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
/// Code-fix for <c>POR001</c> — a <c>{placeholder}</c> the method has no parameter for. Offers a rename
/// to each parameter the method does declare, plus adding the missing parameter.
/// </summary>
/// <remarks>
/// <b>Several actions, none of them presumed.</b> POR001's message already names both remedies — rename
/// the placeholder, or add the parameter — and which one is right is the author's call. Offering the
/// choice is the normal Roslyn shape for a rename suggestion, and it is honest in a way a single guessed
/// action would not be. A method with one parameter therefore gets two actions; a method with none gets
/// only the add.
/// <para>
/// The offending placeholder arrives as a diagnostic property. It has to: the diagnostic's location is
/// the whole route literal, so a route with two bad placeholders produces two diagnostics at one span,
/// and the message is the only other place the name appears — which is exactly what this fix must not
/// read (POR-122).
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RoutePlaceholderCodeFix))]
[Shared]
public sealed class RoutePlaceholderCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(PorticoDiagnostics.RoutePlaceholderMismatch.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        if (!diagnostic.Properties.TryGetValue(RoutePlaceholderAnalyzer.PlaceholderProperty, out var placeholder) ||
            string.IsNullOrEmpty(placeholder))
        {
            return;
        }

        // FindToken(...).Parent, not FindNode(...): the diagnostic's span IS the literal's span, and
        // FindNode ties in favour of the enclosing AttributeArgument — after which AncestorsAndSelf walks
        // upward and never sees the literal at all. The token's parent is the literal, unambiguously.
        var node = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        var literal = node?.AncestorsAndSelf().OfType<LiteralExpressionSyntax>().FirstOrDefault();
        if (literal is null || !literal.IsKind(SyntaxKind.StringLiteralExpression)) return;

        var method = literal.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null) return;

        foreach (var parameter in method.ParameterList.Parameters)
        {
            var target = parameter.Identifier.ValueText;
            if (string.IsNullOrEmpty(target)) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Rename placeholder '{{{placeholder}}}' to '{{{target}}}'",
                    createChangedDocument: ct => RenameAsync(context.Document, literal, placeholder!, target, ct),
                    // Keyed by the target so Fix-all sweeps "rename every stray placeholder to `id`"
                    // as one operation. Renames to different parameters are genuinely different fixes
                    // and must not share a bucket.
                    equivalenceKey: $"{nameof(RoutePlaceholderCodeFix)}.Rename.{target}"),
                diagnostic);
        }

        var identifier = ParameterIdentifier(placeholder!);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Add parameter 'string {identifier}'",
                createChangedDocument: ct => AddParameterAsync(context.Document, method, identifier, ct),
                // One key for every add: a route rename typically leaves several placeholders needing
                // parameters, and sweeping them in one pass is the failure mode POR-122 exists to remove.
                equivalenceKey: $"{nameof(RoutePlaceholderCodeFix)}.AddParameter"),
            diagnostic);
    }

    /// <summary>
    /// Replaces <c>{placeholder}</c> with <c>{target}</c> in the route string, matching only a whole
    /// token.
    /// </summary>
    /// <remarks>
    /// Token-wise, not a substring replace, because the runtime treats <c>{id}</c> as a placeholder only
    /// when it is an entire whitespace-delimited token — <c>user{id}</c> is a literal segment that routes
    /// fine (POR-61). A naive <c>Replace</c> would rewrite the inside of such a literal and change a
    /// route that worked.
    /// </remarks>
    private static async Task<Document> RenameAsync(
        Document document,
        LiteralExpressionSyntax literal,
        string placeholder,
        string target,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var rewritten = string.Join(
            " ",
            literal.Token.ValueText
                .Split(' ')
                .Select(token => token == $"{{{placeholder}}}" ? $"{{{target}}}" : token));

        return document.WithSyntaxRoot(root.ReplaceNode(
            literal,
            CliArgumentRouteCodeFix.StringLiteral(rewritten).WithTriviaFrom(literal)));
    }

    /// <summary>
    /// A placeholder name as a parameter identifier — verbatim (<c>@ref</c>) when the name is a
    /// reserved C# keyword.
    /// </summary>
    /// <remarks>
    /// A route placeholder is constrained only to <c>[A-Za-z_][A-Za-z0-9_]*</c>, so <c>{ref}</c>,
    /// <c>{namespace}</c> and <c>{object}</c> are legal routes — and ordinary names for a command-line
    /// argument. Emitting <c>string ref</c> would hand the author source that does not compile, which
    /// is the one outcome a code fix must never have.
    /// <para>
    /// Reserved words only: <see cref="SyntaxFacts.GetKeywordKind"/> returns <see cref="SyntaxKind.None"/>
    /// for contextual keywords such as <c>value</c>, which are legal parameter names and would only be
    /// made noisier by an <c>@</c>. The verbatim token keeps the bare name as its <i>value</i> text, so
    /// the runtime — which binds on <c>ParameterInfo.Name</c>, and sees <c>ref</c> for <c>@ref</c> —
    /// matches the unchanged route.
    /// </para>
    /// </remarks>
    private static SyntaxToken ParameterIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            ? SyntaxFactory.Identifier(name)
            : SyntaxFactory.Identifier(
                leading: SyntaxTriviaList.Empty,
                contextualKind: SyntaxKind.IdentifierToken,
                text: "@" + name,
                valueText: name,
                trailing: SyntaxTriviaList.Empty);

    /// <summary>
    /// Appends <c>string {name}</c> to the method's parameter list.
    /// </summary>
    /// <remarks>
    /// <c>string</c> because every command-line token arrives as text and a string always binds; the
    /// author narrows the type afterwards if they want to. Appended last, since a parameter's position
    /// in the signature does not determine its position in the route — the route string does, and it
    /// already says where this argument goes (POR-70).
    /// </remarks>
    private static async Task<Document> AddParameterAsync(
        Document document,
        MethodDeclarationSyntax method,
        SyntaxToken name,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var parameter = SyntaxFactory
            .Parameter(name)
            .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword))
                .WithTrailingTrivia(SyntaxFactory.Space));

        return document.WithSyntaxRoot(root.ReplaceNode(
            method.ParameterList,
            method.ParameterList.AddParameters(parameter)));
    }
}

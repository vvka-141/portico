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
/// Code-fix for <c>POR012</c> — rewrites a <c>bool</c> option to <c>CliFlag? … = null</c>.
/// </summary>
/// <remarks>
/// <b>The signature only, deliberately.</b> Portico's contract normally lives on an interface while
/// the body lives on an implementing class, frequently in another file — so a fix that also rewrote
/// <c>if (verbose)</c> to <c>if (verbose is not null)</c> would have to reach across documents it
/// cannot see from the diagnostic, and would be guessing at which of several implementations was
/// meant. Changing the declaration instead produces ordinary compile errors at exactly the sites that
/// need attention, which is the normal shape of a type-change refactor and strictly more honest than
/// a partial rewrite that looks complete.
/// <para>
/// The rule's documentation says this in as many words, so the follow-up is expected rather than a
/// surprise.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BoolUsedAsSwitchCodeFix))]
[Shared]
public sealed class BoolUsedAsSwitchCodeFix : CodeFixProvider
{
    private const string Title = "Change to CliFlag? (presence-only switch)";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(PorticoDiagnostics.BoolUsedAsSwitch.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
        if (node is null) return;

        // The diagnostic points at the [CliOption] attribute; the declaration is its grandparent.
        var parameter = node.AncestorsAndSelf().OfType<ParameterSyntax>().FirstOrDefault();
        var property = node.AncestorsAndSelf().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        if (parameter is null && property is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => parameter is not null
                    ? RewriteParameterAsync(context.Document, parameter, ct)
                    : RewritePropertyAsync(context.Document, property!, ct),
                equivalenceKey: nameof(BoolUsedAsSwitchCodeFix)),
            diagnostic);
    }

    private static async Task<Document> RewriteParameterAsync(
        Document document,
        ParameterSyntax parameter,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        // `CliFlag? x = null` — the null default is what makes the option optional, which is the
        // whole point of a switch: absent means off.
        var rewritten = parameter
            .WithType(CliFlagNullable().WithTriviaFrom(parameter.Type!))
            .WithDefault(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));

        return document.WithSyntaxRoot(root.ReplaceNode(parameter, rewritten));
    }

    private static async Task<Document> RewritePropertyAsync(
        Document document,
        PropertyDeclarationSyntax property,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var rewritten = property.WithType(CliFlagNullable().WithTriviaFrom(property.Type));

        // A `= false` initializer is meaningless on a flag and would not compile against CliFlag?.
        if (property.Initializer is not null)
        {
            rewritten = rewritten.WithInitializer(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));
        }

        return document.WithSyntaxRoot(root.ReplaceNode(property, rewritten));
    }

    private static TypeSyntax CliFlagNullable() =>
        SyntaxFactory.NullableType(SyntaxFactory.IdentifierName("CliFlag"));
}

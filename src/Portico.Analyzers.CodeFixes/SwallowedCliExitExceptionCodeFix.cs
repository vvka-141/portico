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
/// Code-fix for <c>POR013</c> — adds <c>when (ex is not CliExitException)</c> to a catch-all that
/// would otherwise swallow a controlled exit, declaring the <c>ex</c> identifier if the clause has
/// none.
/// </summary>
/// <remarks>
/// Offered only for catch-<em>all</em> clauses. An explicit <c>catch (CliExitException)</c> earns the
/// same warning, but the filter is not its fix — it would make the clause unreachable. That shape is
/// deliberate often enough that the right nudge is the diagnostic alone, and the repair (a
/// <c>throw;</c>, or deleting the clause) depends on what the author meant.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SwallowedCliExitExceptionCodeFix))]
[Shared]
public sealed class SwallowedCliExitExceptionCodeFix : CodeFixProvider
{
    private const string Title = "Exclude CliExitException from this catch";
    private const string ExceptionIdentifier = "ex";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(PorticoDiagnostics.SwallowedCliExitException.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var catchClause = root
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<CatchClauseSyntax>()
            .FirstOrDefault();
        if (catchClause is null) return;

        // `catch (CliExitException)` is reported but not fixed here — see the remarks on this type.
        if (catchClause.Declaration?.Type is IdentifierNameSyntax { Identifier.ValueText: "CliExitException" })
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => AddFilterAsync(context.Document, catchClause, ct),
                equivalenceKey: nameof(SwallowedCliExitExceptionCodeFix)),
            diagnostic);
    }

    private static async Task<Document> AddFilterAsync(
        Document document,
        CatchClauseSyntax catchClause,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        // A bare `catch` has nothing to name in the filter, so the declaration is written too.
        // `catch` and `catch (Exception)` are the same clause to the compiler, so this is a
        // formatting change rather than a semantic one.
        var declaration = catchClause.Declaration is { } existing
            ? existing.WithIdentifier(
                existing.Identifier.IsKind(SyntaxKind.None)
                    ? SyntaxFactory.Identifier(ExceptionIdentifier)
                    : existing.Identifier)
            : SyntaxFactory.CatchDeclaration(
                SyntaxFactory.IdentifierName("Exception"),
                SyntaxFactory.Identifier(ExceptionIdentifier));

        var identifier = declaration.Identifier.ValueText;

        // `ex is not CliExitException`
        var filter = SyntaxFactory.CatchFilterClause(
            SyntaxFactory.IsPatternExpression(
                SyntaxFactory.IdentifierName(identifier),
                SyntaxFactory.UnaryPattern(
                    SyntaxFactory.Token(SyntaxKind.NotKeyword),
                    SyntaxFactory.TypePattern(SyntaxFactory.IdentifierName("CliExitException")))));

        var fixedClause = catchClause
            .WithDeclaration(declaration.WithTrailingTrivia(SyntaxFactory.Space))
            .WithFilter(filter)
            .WithTriviaFrom(catchClause);

        return document.WithSyntaxRoot(root.ReplaceNode(catchClause, fixedClause));
    }
}

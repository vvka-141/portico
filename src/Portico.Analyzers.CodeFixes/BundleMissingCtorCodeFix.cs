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
using Microsoft.CodeAnalysis.Formatting;

namespace Portico.Analyzers;

/// <summary>
/// Code-fix for <c>POR006</c> — adds a <c>public TypeName() { }</c> parameterless constructor to a
/// <c>CliOptions</c> / <c>CliMiddleware</c> subclass that lacks one. Bundles and middleware are
/// instantiated per-invocation via <c>Activator.CreateInstance</c>, which requires a public
/// parameterless constructor.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BundleMissingCtorCodeFix))]
[Shared]
public sealed class BundleMissingCtorCodeFix : CodeFixProvider
{
    private const string Title = "Add public parameterless constructor";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(PorticoDiagnostics.BundleMissingParameterlessCtor.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var typeDecl = root
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();
        if (typeDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => AddConstructorAsync(context.Document, typeDecl, ct),
                equivalenceKey: nameof(BundleMissingCtorCodeFix)),
            diagnostic);
    }

    private static async Task<Document> AddConstructorAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var constructor = SyntaxFactory.ConstructorDeclaration(typeDecl.Identifier.WithoutTrivia())
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList())
            .WithBody(SyntaxFactory.Block())
            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newTypeDecl = typeDecl.WithMembers(typeDecl.Members.Insert(0, constructor));
        return document.WithSyntaxRoot(root.ReplaceNode(typeDecl, newTypeDecl));
    }
}

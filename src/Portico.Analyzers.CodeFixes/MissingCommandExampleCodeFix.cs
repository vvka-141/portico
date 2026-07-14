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
/// Code-fix for <c>POR004</c> — adds a placeholder <c>[CliCommandExample("…", "…")]</c> above a
/// <c>[CliRoute]</c> method that has none. The developer/agent fills in the real verb, arguments,
/// and description, so the inserted example compiles and is immediately runnable.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingCommandExampleCodeFix))]
[Shared]
public sealed class MissingCommandExampleCodeFix : CodeFixProvider
{
    private const string Title = "Add [CliCommandExample] stub";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(PorticoDiagnostics.MissingCommandExample.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var method = root
            .FindToken(diagnostic.Location.SourceSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (method is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => AddExampleAttributeAsync(context.Document, method, ct),
                equivalenceKey: nameof(MissingCommandExampleCodeFix)),
            diagnostic);
    }

    private static async Task<Document> AddExampleAttributeAsync(
        Document document,
        MethodDeclarationSyntax method,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var exampleAttribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("CliCommandExample"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList(new[]
                {
                    StringArgument("TODO: example invocation"),
                    StringArgument("TODO: what this example demonstrates"),
                })));

        var newMethod = CodeFixHelpers.PrependAttribute(method, exampleAttribute);
        return document.WithSyntaxRoot(root.ReplaceNode(method, newMethod));
    }

    private static AttributeArgumentSyntax StringArgument(string value) =>
        SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(value)));
}

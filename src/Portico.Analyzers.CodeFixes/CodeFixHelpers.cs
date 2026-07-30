using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Portico.Analyzers;

/// <summary>
/// Shared helpers for code-fix providers in this assembly. Centralises the trivia-aware
/// "prepend attribute" logic so a single bug-fix lives in one place — fixes that hand-rolled the
/// same pattern produced corrupt formatting when applied to a target that already carried other
/// attributes.
/// </summary>
/// <remarks>
/// It carried type/property/field overloads and using-directive helpers across the extraction; their
/// callers (the DTO / SQLite / claim-check code fixes) stayed behind in the origin, so they had zero
/// callers here and were removed (POR-28). Of the seven fixes now in this assembly, exactly one —
/// POR004's — uses the method overload below; the other six attribute nothing. Re-add an overload
/// when a fix needs it, not in anticipation of one.
/// </remarks>
internal static class CodeFixHelpers
{
    /// <summary>
    /// Returns a new <see cref="MethodDeclarationSyntax"/> with <paramref name="attribute"/> prepended
    /// to its attribute list, preserving the original layout. The doc comment + indentation that
    /// previously belonged to the first existing attribute list is transferred to the new attribute,
    /// and the previously-first attribute list keeps only its indent.
    /// </summary>
    /// <remarks>
    /// The method is expected to already carry at least one attribute list — POR004's fix only runs on
    /// a <c>[CliRoute]</c> method, which by definition has one. A no-attribute overload was removed as
    /// unreachable (POR-66); re-add it when a fix actually needs to attribute a bare method.
    /// </remarks>
    internal static MethodDeclarationSyntax PrependAttribute(MethodDeclarationSyntax method, AttributeSyntax attribute)
    {
        var newList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        var firstExisting = method.AttributeLists[0];
        var (docAndComments, indent) = SplitLeadingTrivia(firstExisting.GetLeadingTrivia());

        // Doc/comments stay above; the new attribute is indented to match the first existing
        // attribute. The previously-first attribute keeps only its indent.
        var newWithLeading = newList
            .WithLeadingTrivia(docAndComments.AddRange(indent))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        var firstStripped = firstExisting.WithLeadingTrivia(indent);

        var newAttrLists = SyntaxFactory.List(
            new[] { newWithLeading, firstStripped }.Concat(method.AttributeLists.Skip(1)));

        return method.WithAttributeLists(newAttrLists);
    }

    /// <summary>
    /// Splits a leading-trivia list into "doc comments + line comments + blank lines" (everything
    /// before the final whitespace run) and "indent" (the trailing whitespace immediately preceding
    /// the consumer). This boundary is what makes the prepend behaviour preserve doc-comment
    /// placement: the doc/comments belong above the (new) topmost attribute, the indent belongs
    /// immediately before each of the attributes.
    /// </summary>
    private static (SyntaxTriviaList DocAndComments, SyntaxTriviaList Indent) SplitLeadingTrivia(SyntaxTriviaList trivia)
    {
        // Walk backwards to find the boundary. Whitespace at the tail is the indent; everything
        // before it (including blank-line whitespace separated by newlines) belongs to the
        // doc/comments block.
        var splitIdx = trivia.Count;
        for (var i = trivia.Count - 1; i >= 0; i--)
        {
            if (trivia[i].IsKind(SyntaxKind.WhitespaceTrivia)) continue;
            splitIdx = i + 1;
            break;
        }
        if (splitIdx == trivia.Count) splitIdx = 0; // entire list is whitespace

        return (
            SyntaxFactory.TriviaList(trivia.Take(splitIdx)),
            SyntaxFactory.TriviaList(trivia.Skip(splitIdx)));
    }
}

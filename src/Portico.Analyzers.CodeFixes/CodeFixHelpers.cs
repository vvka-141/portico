using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Portico.Analyzers;

/// <summary>
/// Shared helpers for code-fix providers in this assembly. Centralises the
/// trivia-aware "prepend attribute" logic so a single bug-fix lives in one
/// place — fixes that hand-rolled the same pattern produced corrupt formatting
/// when applied to a target that already carried other attributes.
/// </summary>
internal static class CodeFixHelpers
{
    /// <summary>
    /// Returns a new <see cref="TypeDeclarationSyntax"/> with
    /// <paramref name="attribute"/> prepended to its attribute list, preserving
    /// the original layout. The doc comment + indentation that previously
    /// belonged to the first existing attribute list is transferred to the
    /// new attribute, and the previously-first attribute list keeps only its
    /// indent. When the type has no existing attributes, the type's own
    /// leading trivia (typically the doc comment + indent) is moved onto the
    /// new attribute list.
    /// </summary>
    internal static TypeDeclarationSyntax PrependAttribute(TypeDeclarationSyntax type, AttributeSyntax attribute)
    {
        var newList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        if (type.AttributeLists.Count == 0)
        {
            // No existing attributes: hoist the type's leading trivia onto the
            // new list and strip it from the type.
            return type
                .WithLeadingTrivia(SyntaxFactory.TriviaList())
                .WithAttributeLists(SyntaxFactory.SingletonList(
                    newList
                        .WithLeadingTrivia(type.GetLeadingTrivia())
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)));
        }

        var firstExisting = type.AttributeLists[0];
        var (docAndComments, indent) = SplitLeadingTrivia(firstExisting.GetLeadingTrivia());

        // Doc/comments stay above; the new attribute is indented to match the
        // first existing attribute. The previously-first attribute keeps only
        // its indent (the doc/comments are now above the new attribute).
        var newWithLeading = newList
            .WithLeadingTrivia(docAndComments.AddRange(indent))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        var firstStripped = firstExisting.WithLeadingTrivia(indent);

        var newAttrLists = SyntaxFactory.List(
            new[] { newWithLeading, firstStripped }.Concat(type.AttributeLists.Skip(1)));

        return type.WithAttributeLists(newAttrLists);
    }

    /// <summary>
    /// Same shape as <see cref="PrependAttribute(TypeDeclarationSyntax, AttributeSyntax)"/> for a method declaration.
    /// </summary>
    internal static MethodDeclarationSyntax PrependAttribute(MethodDeclarationSyntax method, AttributeSyntax attribute)
    {
        var newList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        if (method.AttributeLists.Count == 0)
        {
            return method
                .WithLeadingTrivia(SyntaxFactory.TriviaList())
                .WithAttributeLists(SyntaxFactory.SingletonList(
                    newList
                        .WithLeadingTrivia(method.GetLeadingTrivia())
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)));
        }

        var firstExisting = method.AttributeLists[0];
        var (docAndComments, indent) = SplitLeadingTrivia(firstExisting.GetLeadingTrivia());

        var newWithLeading = newList
            .WithLeadingTrivia(docAndComments.AddRange(indent))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        var firstStripped = firstExisting.WithLeadingTrivia(indent);

        var newAttrLists = SyntaxFactory.List(
            new[] { newWithLeading, firstStripped }.Concat(method.AttributeLists.Skip(1)));

        return method.WithAttributeLists(newAttrLists);
    }

    /// <summary>
    /// Same shape as <see cref="PrependAttribute(TypeDeclarationSyntax, AttributeSyntax)"/> for a property declaration.
    /// </summary>
    internal static PropertyDeclarationSyntax PrependAttribute(PropertyDeclarationSyntax property, AttributeSyntax attribute)
    {
        var newList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        if (property.AttributeLists.Count == 0)
        {
            return property
                .WithLeadingTrivia(SyntaxFactory.TriviaList())
                .WithAttributeLists(SyntaxFactory.SingletonList(
                    newList
                        .WithLeadingTrivia(property.GetLeadingTrivia())
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)));
        }

        var firstExisting = property.AttributeLists[0];
        var (docAndComments, indent) = SplitLeadingTrivia(firstExisting.GetLeadingTrivia());

        var newWithLeading = newList
            .WithLeadingTrivia(docAndComments.AddRange(indent))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        var firstStripped = firstExisting.WithLeadingTrivia(indent);

        var newAttrLists = SyntaxFactory.List(
            new[] { newWithLeading, firstStripped }.Concat(property.AttributeLists.Skip(1)));

        return property.WithAttributeLists(newAttrLists);
    }

    /// <summary>
    /// Same shape as <see cref="PrependAttribute"/> for a field declaration.
    /// </summary>
    internal static FieldDeclarationSyntax PrependAttribute(FieldDeclarationSyntax field, AttributeSyntax attribute)
    {
        var newList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        if (field.AttributeLists.Count == 0)
        {
            return field
                .WithLeadingTrivia(SyntaxFactory.TriviaList())
                .WithAttributeLists(SyntaxFactory.SingletonList(
                    newList
                        .WithLeadingTrivia(field.GetLeadingTrivia())
                        .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)));
        }

        var firstExisting = field.AttributeLists[0];
        var (docAndComments, indent) = SplitLeadingTrivia(firstExisting.GetLeadingTrivia());

        var newWithLeading = newList
            .WithLeadingTrivia(docAndComments.AddRange(indent))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        var firstStripped = firstExisting.WithLeadingTrivia(indent);

        var newAttrLists = SyntaxFactory.List(
            new[] { newWithLeading, firstStripped }.Concat(field.AttributeLists.Skip(1)));

        return field.WithAttributeLists(newAttrLists);
    }

    /// <summary>
    /// Splits a leading-trivia list into "doc comments + line comments + blank
    /// lines" (everything before the final whitespace run) and "indent" (the
    /// trailing whitespace immediately preceding the consumer). This boundary
    /// is what makes the prepend behaviour preserve doc-comment placement: the
    /// doc/comments belong above the (new) topmost attribute, the indent
    /// belongs immediately before each of the attributes.
    /// </summary>
    private static (SyntaxTriviaList DocAndComments, SyntaxTriviaList Indent) SplitLeadingTrivia(SyntaxTriviaList trivia)
    {
        // Walk backwards to find the boundary. Whitespace at the tail is the
        // indent; everything before it (including blank-line whitespace
        // separated by newlines) belongs to the doc/comments block.
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

    /// <summary>True when the compilation unit already has a using directive for the named namespace.</summary>
    internal static bool HasUsing(CompilationUnitSyntax compilationUnit, string namespaceName)
    {
        foreach (var usingDirective in compilationUnit.Usings)
        {
            if (usingDirective.Name?.ToString() == namespaceName)
                return true;
        }
        return false;
    }

    /// <summary>Adds <paramref name="namespaceName"/> as a using directive to the compilation unit.</summary>
    internal static CompilationUnitSyntax AddUsing(CompilationUnitSyntax compilationUnit, string namespaceName)
    {
        var newUsing = SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        return compilationUnit.AddUsings(newUsing);
    }
}

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR013 — a <c>catch</c> clause inside a command handler swallows <c>CliExitException</c>.
/// </summary>
/// <remarks>
/// The first body-level rule in the suite. Every other analyzer registers on the declaration that
/// carries the attribute; this one starts from a <c>catch</c> clause in an <em>implementing</em>
/// class and resolves backwards to the method that carries <c>[CliRoute]</c> — which, for a
/// contract-first CLI, is usually on an interface the class implements.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SwallowedCliExitExceptionAnalyzer : DiagnosticAnalyzer
{
    private const string CliRouteAttributeFullName = "Portico.CliRouteAttribute";
    private const string CliExitExceptionFullName = "Portico.CliExitException";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.SwallowedCliExitException);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;

        // A `when` filter means the author considered which exceptions this clause takes. Whether the
        // filter actually excludes CliExitException is not decidable in general, and this rule is not
        // worth a false positive to find out — see the analyzer principle in docs/reference.
        if (catchClause.Filter is not null) return;

        // A bare `throw;` re-raises whatever was caught, so the exit still reaches the boundary.
        if (RethrowsUnconditionally(catchClause)) return;

        var method = catchClause.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is null) return;

        var exitExceptionType = context.Compilation.GetTypeByMetadataName(CliExitExceptionFullName);
        if (exitExceptionType is null) return;   // not a Portico compilation

        if (!CatchesExitException(context, catchClause, exitExceptionType, out var caughtTypeName)) return;
        if (!IsCommandHandler(context, method)) return;

        var clauseText = caughtTypeName.Length == 0 ? "catch" : $"catch ({caughtTypeName})";

        context.ReportDiagnostic(Diagnostic.Create(
            PorticoDiagnostics.SwallowedCliExitException,
            catchClause.Declaration is { } declaration
                ? declaration.GetLocation()
                : catchClause.CatchKeyword.GetLocation(),
            clauseText,
            method.Identifier.ValueText));
    }

    /// <summary>
    /// Whether this clause would catch a <c>CliExitException</c>: no declaration at all
    /// (<c>catch { }</c>), or a declared type that <c>CliExitException</c> is assignable to.
    /// </summary>
    private static bool CatchesExitException(
        SyntaxNodeAnalysisContext context,
        CatchClauseSyntax catchClause,
        INamedTypeSymbol exitExceptionType,
        out string caughtTypeName)
    {
        if (catchClause.Declaration is null)
        {
            caughtTypeName = "";     // a bare `catch { }`; the caller renders it without parentheses
            return true;
        }

        var caught = context.SemanticModel
            .GetTypeInfo(catchClause.Declaration.Type, context.CancellationToken).Type;
        caughtTypeName = caught?.Name ?? "";

        for (var type = (ITypeSymbol?)exitExceptionType; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, caught)) return true;
        }

        return false;
    }

    /// <summary>
    /// A bare <c>throw;</c> somewhere in the clause body, ignoring any nested <c>catch</c> — a
    /// rethrow inside an inner handler re-raises that inner exception, not this one.
    /// </summary>
    private static bool RethrowsUnconditionally(CatchClauseSyntax catchClause) =>
        catchClause.Block
            .DescendantNodes(descendIntoChildren: node => node is not CatchClauseSyntax)
            .OfType<ThrowStatementSyntax>()
            .Any(t => t.Expression is null);

    /// <summary>
    /// Whether the method is a Portico command handler: it carries <c>[CliRoute]</c> itself, or it
    /// implements an interface method that does.
    /// </summary>
    /// <remarks>
    /// Both halves are load-bearing, and each covers a registration path the other misses.
    /// <c>AddCommands(new Tool())</c> against a class whose own methods carry <c>[CliRoute]</c> has no
    /// interface to walk to; the contract-first shape puts the attribute on the interface and leaves
    /// the implementing method bare, which is where the body — and the <c>catch</c> — actually is.
    /// <para>
    /// A <b>type-level</b> <c>[CliRoute]</c> is deliberately not enough. It is a route prefix, not a
    /// command declaration: verified by running a type-level-only method through
    /// <c>CliApplication</c>, which reports "Unknown command". Treating it as a handler would report
    /// on every method of a prefixed class.
    /// </para>
    /// </remarks>
    private static bool IsCommandHandler(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method)
    {
        if (HasCliRouteAttribute(context, method.AttributeLists)) return true;

        if (context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is not { } symbol)
        {
            return false;
        }

        // Explicit implementations name their interface member directly.
        foreach (var explicitlyImplemented in symbol.ExplicitInterfaceImplementations)
        {
            if (HasCliRouteAttribute(explicitlyImplemented)) return true;
        }

        // Implicit implementations: any interface the containing type declares may be the contract,
        // and only one of several needs to carry the route.
        var containingType = symbol.ContainingType;
        if (containingType is null) return false;

        foreach (var interfaceType in containingType.AllInterfaces)
        {
            foreach (var interfaceMember in interfaceType.GetMembers().OfType<IMethodSymbol>())
            {
                if (!HasCliRouteAttribute(interfaceMember)) continue;

                var implementation = containingType.FindImplementationForInterfaceMember(interfaceMember);
                if (SymbolEqualityComparer.Default.Equals(implementation, symbol)) return true;
            }
        }

        return false;
    }

    private static bool HasCliRouteAttribute(ISymbol symbol) =>
        symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == CliRouteAttributeFullName);

    private static bool HasCliRouteAttribute(
        SyntaxNodeAnalysisContext context,
        SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attribute in attributeLists.SelectMany(list => list.Attributes))
        {
            var symbol = context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol;
            if (symbol?.ContainingType?.ToDisplayString() == CliRouteAttributeFullName) return true;
        }
        return false;
    }
}

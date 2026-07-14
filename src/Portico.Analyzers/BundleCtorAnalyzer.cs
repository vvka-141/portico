using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR006 — any class that transitively extends <c>CliOptions</c> or <c>CliMiddleware</c>
/// must have a public parameterless constructor. The framework instantiates bundles and
/// middleware via <c>Activator.CreateInstance(Type)</c> on every command invocation; a
/// parameter-taking ctor would throw <c>MissingMethodException</c> at dispatch.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BundleCtorAnalyzer : DiagnosticAnalyzer
{
    private const string CliOptionsFullName = "Portico.CliOptions";
    private const string CliMiddlewareFullName = "Portico.CliMiddleware";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.BundleMissingParameterlessCtor);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;

        // Abstract bundles don't need a parameterless ctor — concrete subclasses do.
        if (classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword))) return;

        var symbol = context.SemanticModel.GetDeclaredSymbol(classDecl, context.CancellationToken);
        if (symbol is null || symbol.IsAbstract) return;

        var bundleBase = FindBundleBase(symbol);
        if (bundleBase is null) return;

        // If no constructors are declared, C# synthesizes a public parameterless one → OK.
        var ctors = symbol.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared).ToArray();
        if (ctors.Length == 0) return;

        var hasPublicParameterless = ctors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
        if (hasPublicParameterless) return;

        // Locate the class's identifier for the diagnostic squiggle.
        context.ReportDiagnostic(Diagnostic.Create(
            PorticoDiagnostics.BundleMissingParameterlessCtor,
            classDecl.Identifier.GetLocation(),
            symbol.Name,
            bundleBase));
    }

    /// <summary>
    /// Walks the type's base chain. Returns <c>"CliMiddleware"</c>, <c>"CliOptions"</c>, or
    /// <c>null</c> when neither is in the inheritance hierarchy.
    /// </summary>
    private static string? FindBundleBase(INamedTypeSymbol symbol)
    {
        var type = symbol.BaseType;
        while (type is not null)
        {
            var name = type.ToDisplayString();
            if (name == CliMiddlewareFullName) return "CliMiddleware";
            if (name == CliOptionsFullName) return "CliOptions";
            type = type.BaseType;
        }
        return null;
    }
}

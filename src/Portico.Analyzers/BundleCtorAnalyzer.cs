using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR006 — a <c>CliOptions</c> <b>bundle</b> must have a public parameterless constructor. The
/// framework instantiates bundles via <c>Activator.CreateInstance(Type)</c> on every command
/// invocation (<c>CliOptionsParameterInfo</c>), so a parameter-taking ctor throws
/// <c>MissingMethodException</c> at dispatch.
/// <para>
/// <b><c>CliMiddleware</c> is deliberately exempt</b>, even though it inherits from
/// <c>CliOptions</c>. Middleware is never constructed by the framework — the user supplies an
/// instance to <c>UseMiddleware(...)</c>, and it is <c>MemberwiseClone</c>d per dispatch, which
/// preserves constructor-injected fields. Flagging it forbade the ordinary DI shape
/// (<c>UseMiddleware(sp.GetRequiredService&lt;T&gt;())</c>) that the runtime has always supported.
/// The two base classes have different lifecycles; only the bundle one is Activator-constructed.
/// </para>
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

        // Middleware is user-constructed and cloned, never Activator-constructed. A ctor dependency
        // is legitimate — and is what a DI container hands to UseMiddleware(...).
        if (bundleBase == "CliMiddleware") return;

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

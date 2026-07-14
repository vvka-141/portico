using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR010 — a <c>[CliOption]</c> whose type cannot be built from a command-line string. Everything a
/// user types is text; Portico binds it through <c>TypeDescriptor</c>, so an option's type needs a
/// <c>TypeConverter</c> that converts from string. The runtime rejects this at
/// <c>CliApplication.Create</c> and remains the backstop.
/// </summary>
/// <remarks>
/// <para>
/// <b>This rule is deliberately conservative, and that is the whole design.</b> Whether an arbitrary
/// type has a converter is a <em>runtime</em> fact: <c>TypeDescriptor</c> carries an intrinsic table
/// (<c>Guid</c>, <c>TimeSpan</c>, <c>Uri</c>, <c>DateTime</c>, …) that is invisible to Roslyn, and a
/// converter can also arrive by attribute, by inheritance, or from a provider registered at startup.
/// A rule that guessed would eventually fail a build that works — strictly worse than the runtime
/// error it set out to replace.
/// </para>
/// <para>
/// So it fires only where the answer is certain: a type <em>declared in this compilation</em> — the
/// user's own class or struct — that carries no <c>[TypeConverter]</c> anywhere in its hierarchy and
/// is not an enum. That is the case the ticket was filed for (a <c>Money</c> class as an option
/// type), and it cannot false-positive on a BCL type whose converter Roslyn cannot see. Anything
/// referenced from metadata is left to the runtime.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnconvertibleOptionTypeAnalyzer : DiagnosticAnalyzer
{
    private const string CliOptionAttributeFullName = "Portico.CliOptionAttribute";
    private const string TypeConverterAttributeFullName = "System.ComponentModel.TypeConverterAttribute";

    /// <summary>
    /// Types the framework binds without a converter at all. <c>CliFlag</c> is presence-only — its
    /// materializer never sees a string, so it has no <c>TypeConverter</c> and needs none.
    /// <b>This rule fired on Portico's own <c>CliTimingMiddleware</c> before this list existed</b>,
    /// which is the argument for running a new analyzer over the framework itself before shipping it.
    /// </summary>
    private static readonly ImmutableHashSet<string> BoundWithoutAConverter =
        ImmutableHashSet.Create("Portico.CliFlag", "System.Threading.CancellationToken");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.UnconvertibleOptionType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeParameter, SymbolKind.Parameter);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeParameter(SymbolAnalysisContext context)
    {
        var parameter = (IParameterSymbol)context.Symbol;
        Analyze(context, parameter, parameter.Type);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        Analyze(context, property, property.Type);
    }

    private static void Analyze(SymbolAnalysisContext context, ISymbol symbol, ITypeSymbol declaredType)
    {
        var option = symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == CliOptionAttributeFullName);
        if (option is null) return;

        var valueType = ElementType(Unwrap(declaredType));
        if (valueType is null || !IsCertainlyUnconvertible(valueType)) return;

        var location = option.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                       ?? symbol.Locations.FirstOrDefault();
        if (location is null) return;

        context.ReportDiagnostic(Diagnostic.Create(
            PorticoDiagnostics.UnconvertibleOptionType,
            location,
            AliasesOf(option, symbol),
            valueType.ToDisplayString()));
    }

    /// <summary>The alias list as written, for the message — or the member's name if it has none.</summary>
    private static string AliasesOf(AttributeData option, ISymbol symbol) =>
        option.ConstructorArguments.Length > 0 && option.ConstructorArguments[0].Value is string spec
            ? spec
            : symbol.Name;

    /// <summary><c>T?</c> binds exactly as <c>T</c> does.</summary>
    private static ITypeSymbol Unwrap(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    /// <summary>
    /// The type a command-line token actually converts into: the element of a collection, the value
    /// of a <c>string</c>-keyed map, or the type itself. Mirrors the recursion in
    /// <c>CliOptionAttribute.CanAccept</c>.
    /// </summary>
    private static ITypeSymbol? ElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return Unwrap(array.ElementType);

        if (type is not INamedTypeSymbol named || !named.IsGenericType) return type;

        var definition = named.ConstructedFrom.ToDisplayString();

        // A map option (--cfg[env] prod) binds its VALUES from the command line; the key is a string.
        if (definition.StartsWith("System.Collections.Generic.IDictionary<", System.StringComparison.Ordinal) ||
            definition.StartsWith("System.Collections.Generic.Dictionary<", System.StringComparison.Ordinal) ||
            definition.StartsWith("System.Collections.Generic.IReadOnlyDictionary<", System.StringComparison.Ordinal))
        {
            return named.TypeArguments.Length == 2 ? Unwrap(named.TypeArguments[1]) : null;
        }

        // Any single-argument generic collection shape: List<T>, HashSet<T>, IEnumerable<T>, …
        if (named.TypeArguments.Length == 1 && ImplementsEnumerable(named))
        {
            return Unwrap(named.TypeArguments[0]);
        }

        // Some other generic type. Not something this rule is confident about.
        return type;
    }

    // OriginalDefinition, not the constructed symbol: SpecialType lives on IEnumerable<T>, and a
    // constructed IEnumerable<Money> reports SpecialType.None. Comparing the constructed symbol
    // silently matched nothing, and List<Money> sailed through.
    private static bool ImplementsEnumerable(INamedTypeSymbol type) =>
        type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ||
        type.AllInterfaces.Any(i =>
            i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);

    /// <summary>
    /// True only when the type is one the analyzer can be SURE has no string converter: declared in
    /// this compilation, not an enum, and carrying no <c>[TypeConverter]</c> in its hierarchy.
    /// Everything else — every referenced type — is left to the runtime, on purpose.
    /// </summary>
    private static bool IsCertainlyUnconvertible(ITypeSymbol type)
    {
        if (BoundWithoutAConverter.Contains(type.ToDisplayString())) return false;
        if (type.TypeKind == TypeKind.Enum) return false;                 // enums convert from their name
        if (type.SpecialType != SpecialType.None) return false;           // string, int, bool, …
        if (type.TypeKind == TypeKind.TypeParameter) return false;        // unresolved generic
        if (type.DeclaringSyntaxReferences.IsEmpty) return false;         // from metadata: cannot know

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return false;

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == TypeConverterAttributeFullName))
            {
                return false;
            }
        }

        return true;
    }
}

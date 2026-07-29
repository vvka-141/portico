using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR012 — a <c>[CliOption]</c> on a <c>bool</c> (or <c>bool?</c>) is probably meant to be a switch.
/// </summary>
/// <remarks>
/// <para>
/// The framework's own reference calls this its most common misuse, which makes it a pit of failure:
/// <c>[CliOption("--verbose")] bool verbose</c> compiles, runs, and produces a CLI where
/// <c>--verbose</c> alone does not work. The operator decision of 2026-07-24 was to keep
/// <c>CliFlag?</c> rather than reinterpret <c>bool</c> as a switch — <c>--flag=false</c> has no
/// coherent meaning, and a switch often implies a different set of legal options, which a value type
/// cannot model. Given that, the answer to a pit of failure is a diagnostic at the edge.
/// </para>
/// <para>
/// <c>bool?</c> is in scope and gets the same message. It is if anything a stronger signal: a
/// three-state value is almost never what a command line wants, and an author reaching for it is
/// usually trying to express "absent" — which is exactly what <c>CliFlag?</c> already means.
/// </para>
/// <para>
/// Both the parameter and the <c>CliOptions</c> bundle-property paths are covered, because the two
/// have drifted before (POR-59) and the mistake is identical in each.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BoolUsedAsSwitchAnalyzer : DiagnosticAnalyzer
{
    private const string CliOptionAttributeFullName = "Portico.CliOptionAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.BoolUsedAsSwitch);

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

        if (!IsBoolean(declaredType)) return;

        var location = option.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                       ?? symbol.Locations.FirstOrDefault();
        if (location is null) return;

        context.ReportDiagnostic(Diagnostic.Create(
            PorticoDiagnostics.BoolUsedAsSwitch,
            location,
            PrimaryAlias(option, symbol),
            declaredType.ToDisplayString(),
            symbol.Name));
    }

    /// <summary><c>bool</c> or <c>bool?</c>. A collection of bools is a different mistake, not this one.</summary>
    private static bool IsBoolean(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Boolean) return true;

        return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
               && nullable.TypeArguments.Length == 1
               && nullable.TypeArguments[0].SpecialType == SpecialType.System_Boolean;
    }

    /// <summary>
    /// The first alias as written, so the message can show the option the way the user types it
    /// (<c>--force true</c>) rather than the way it is declared.
    /// </summary>
    private static string PrimaryAlias(AttributeData option, ISymbol symbol)
    {
        if (option.ConstructorArguments.Length == 0 ||
            option.ConstructorArguments[0].Value is not string spec ||
            string.IsNullOrWhiteSpace(spec))
        {
            return symbol.Name;
        }

        var first = spec.Split('|').FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));
        return first?.Trim() ?? symbol.Name;
    }
}

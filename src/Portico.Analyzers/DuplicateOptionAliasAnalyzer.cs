using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR009 — two <c>[CliOption]</c> declarations on the same command claim the same alias. Mirrors
/// <c>CliMethodInfo.RejectDuplicateOptionAliases</c>, which rejects the same shape at
/// <c>CliApplication.Create</c>; the runtime check stays as the backstop for builds without the
/// analyzer.
/// </summary>
/// <remarks>
/// <para>
/// Covers direct <c>[CliOption]</c> parameters, the <c>[CliOption]</c> properties of a
/// <c>CliOptions</c> bundle parameter, and collisions <em>between</em> the two — the sneaky case,
/// because the two declarations sit in different files.
/// </para>
/// <para>
/// <b>The comparison rule is duplicated from <c>CliAliasComparer</c> and must not drift from it.</b>
/// A single-character short alias is case-SENSITIVE (<c>-v</c> and <c>-V</c> are different options —
/// the curl idiom); anything longer is case-insensitive (<c>--name</c> and <c>--NAME</c> collide).
/// The analyzer assembly cannot reference the framework, so the rule lives in two places; if you
/// change one, change the other, or the compiler and the runtime will disagree about what a
/// duplicate is.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateOptionAliasAnalyzer : DiagnosticAnalyzer
{
    private const string CliOptionAttributeFullName = "Portico.CliOptionAttribute";
    private const string CliRouteAttributeFullName = "Portico.CliRouteAttribute";
    private const string CliOptionsFullName = "Portico.CliOptions";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.DuplicateOptionAlias);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodSyntax = (MethodDeclarationSyntax)context.Node;
        var method = context.SemanticModel.GetDeclaredSymbol(methodSyntax, context.CancellationToken);
        if (method is null || !HasAttribute(method, CliRouteAttributeFullName)) return;

        // alias -> the declaration that claimed it first.
        var claimed = new Dictionary<string, string>(AliasComparer.Instance);

        foreach (var parameter in method.Parameters)
        {
            var option = FindAttribute(parameter, CliOptionAttributeFullName);
            if (option is not null)
            {
                Check(context, option, $"parameter '{parameter.Name}'", claimed, parameter);
                continue;
            }

            // A CliOptions bundle contributes every [CliOption] property it declares.
            if (parameter.Type is not INamedTypeSymbol bundle || !DerivesFromCliOptions(bundle)) continue;

            foreach (var property in BundleOptions(bundle))
            {
                var propertyOption = FindAttribute(property, CliOptionAttributeFullName);
                if (propertyOption is null) continue;

                Check(
                    context,
                    propertyOption,
                    $"property '{property.Name}' of bundle '{bundle.Name}'",
                    claimed,
                    // The bundle property may live in another file. Report on the parameter that
                    // dragged it in — that is the line the user can actually act on.
                    parameter);
            }
        }
    }

    private static void Check(
        SyntaxNodeAnalysisContext context,
        AttributeData option,
        string origin,
        Dictionary<string, string> claimed,
        ISymbol reportOn)
    {
        foreach (var alias in Aliases(option))
        {
            if (claimed.TryGetValue(alias, out var firstOwner))
            {
                var location = AttributeLocation(option, context) ?? reportOn.Locations.FirstOrDefault();
                if (location is null) continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    PorticoDiagnostics.DuplicateOptionAlias,
                    location,
                    alias,
                    firstOwner,
                    origin));
                continue;
            }

            claimed[alias] = origin;
        }
    }

    /// <summary>The pipe-separated alias list of a <c>[CliOption("--name|-n")]</c>, first argument.</summary>
    private static IEnumerable<string> Aliases(AttributeData option)
    {
        if (option.ConstructorArguments.Length == 0) return [];
        if (option.ConstructorArguments[0].Value is not string spec) return [];

        return spec
            .Split('|')
            .Select(alias => alias.Trim())
            .Where(alias => alias.Length > 0);
    }

    private static Location? AttributeLocation(AttributeData attribute, SyntaxNodeAnalysisContext context)
    {
        var reference = attribute.ApplicationSyntaxReference;
        if (reference is null) return null;

        // Only report inside the tree being analysed; a bundle property in another file would
        // otherwise produce a diagnostic Roslyn refuses to attach.
        return reference.SyntaxTree == context.Node.SyntaxTree
            ? reference.GetSyntax(context.CancellationToken).GetLocation()
            : null;
    }

    private static IEnumerable<IPropertySymbol> BundleOptions(INamedTypeSymbol bundle)
    {
        for (var type = bundle; type is not null; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.DeclaredAccessibility == Accessibility.Public)
                {
                    yield return property;
                }
            }
        }
    }

    private static bool DerivesFromCliOptions(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == CliOptionsFullName) return true;
        }
        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string fullName) =>
        FindAttribute(symbol, fullName) is not null;

    private static AttributeData? FindAttribute(ISymbol symbol, string fullName) =>
        symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fullName);

    /// <summary>
    /// The framework's alias-equality rule, mirrored: a one-character short alias (<c>-v</c>) is
    /// case-sensitive; everything longer is case-insensitive. Kept in lockstep with
    /// <c>Portico.CliAliasComparer</c> — see the remarks on the analyzer.
    /// </summary>
    private sealed class AliasComparer : IEqualityComparer<string>
    {
        public static readonly AliasComparer Instance = new();

        public bool Equals(string x, string y) =>
            string.Equals(Normalize(x), Normalize(y), StringComparison.Ordinal);

        public int GetHashCode(string obj) => Normalize(obj).GetHashCode();

        // "-v" is two characters: a dash and the short name. Longer forms fold to lower case.
        private static string Normalize(string alias) =>
            IsShortForm(alias) ? alias : alias.ToLowerInvariant();

        private static bool IsShortForm(string alias) =>
            alias.Length == 2 && alias[0] == '-';
    }
}

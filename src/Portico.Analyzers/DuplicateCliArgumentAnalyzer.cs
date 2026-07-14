using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR007 — reports a method parameter targeted by more than one <c>[CliArgument]</c>: two
/// method-level <c>nameof</c> references to the same parameter, two parameter-level attributes,
/// or a method-level + parameter-level pair. The framework binds exactly one, so the extras
/// silently misbind.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateCliArgumentAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.DuplicateCliArgument);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // target parameter name -> reported locations of every [CliArgument] aimed at it.
        var targets = new Dictionary<string, List<Location>>();

        // Method-level [CliArgument(nameof(x), "desc")] forms.
        foreach (var attribute in method.AttributeLists.SelectMany(al => al.Attributes))
        {
            if (!CliArgumentAttributeFacts.TryGetMethodLevelParameterName(
                    context, attribute, out var parameterNameExpression))
            {
                continue;
            }

            var constant = context.SemanticModel.GetConstantValue(parameterNameExpression, context.CancellationToken);
            if (constant is { HasValue: true, Value: string target })
            {
                Add(targets, target, attribute.GetLocation());
            }
        }

        // Parameter-level [CliArgument] forms.
        foreach (var parameter in method.ParameterList.Parameters)
        {
            var name = parameter.Identifier.ValueText;
            foreach (var attribute in parameter.AttributeLists.SelectMany(al => al.Attributes))
            {
                if (CliArgumentAttributeFacts.IsCliArgument(context, attribute))
                {
                    Add(targets, name, attribute.GetLocation());
                }
            }
        }

        foreach (var entry in targets)
        {
            var locations = entry.Value;
            if (locations.Count <= 1) continue;

            context.ReportDiagnostic(Diagnostic.Create(
                PorticoDiagnostics.DuplicateCliArgument,
                locations[locations.Count - 1],
                entry.Key,
                method.Identifier.ValueText,
                locations.Count));
        }
    }

    private static void Add(Dictionary<string, List<Location>> targets, string key, Location location)
    {
        if (!targets.TryGetValue(key, out var list))
        {
            list = new List<Location>();
            targets[key] = list;
        }
        list.Add(location);
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR007 — reports a method parameter carrying more than one <c>[CliArgument]</c>. The framework
/// binds exactly one, so the extras are silently discarded along with their descriptions.
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

        // target parameter name -> reported locations of every [CliArgument] on it.
        var targets = new Dictionary<string, List<Location>>();

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

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Portico.Analyzers;

namespace Portico;

/// <summary>
/// The set of rule IDs the analyzer assembly actually reports, discovered by reflection.
/// </summary>
/// <remarks>
/// Shared by every guard that has to stay in step with the rule set —
/// <see cref="PorticoAnalyzerDocs_Should"/> (the four documentation tables) and
/// <see cref="PorticoRuntimeBackstops_Should"/> (the runtime half of each rule). Reflection over
/// the assembly rather than a list written here: a new analyzer class is exactly the thing that
/// would be forgotten, and a hand-maintained list would agree with the omission forever, which is
/// the drift being guarded against one layer up.
/// </remarks>
internal static class PorticoAnalyzerRules
{
    internal static IReadOnlyCollection<string> LiveIds() =>
        // Any analyzer type serves as the handle on the assembly; the descriptors themselves live on
        // an internal PorticoDiagnostics that only the code fixes can see.
        typeof(UnconvertibleOptionTypeAnalyzer).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .Select(descriptor => descriptor.Id)
            // POR009 is declared by two descriptors — one for method parameters, one for bundle
            // properties — and is one rule to a reader.
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The rule IDs a code-fix provider actually offers to fix, discovered the same way.
    /// </summary>
    /// <remarks>
    /// The providers live in a second assembly (Workspaces is a code-fix-only dependency, kept out of
    /// the analyzer DLL for RS1038), so this needs its own handle on it. Reflection for the same
    /// reason as <see cref="LiveIds"/>: a new provider is what gets forgotten, and POR-122 added three
    /// of them at once while the reference page kept saying six rules had a fix.
    /// </remarks>
    internal static IReadOnlyCollection<string> FixableIds() =>
        typeof(RoutePlaceholderCodeFix).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(CodeFixProvider).IsAssignableFrom(type))
            .Select(type => (CodeFixProvider)Activator.CreateInstance(type)!)
            .SelectMany(provider => provider.FixableDiagnosticIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
}

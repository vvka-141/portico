using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Portico.Analyzers;

/// <summary>
/// POR002 — two or more methods in the compilation declare the same <c>[CliRoute]</c>
/// signature (after whitespace normalization). The framework's runtime duplicate-route check
/// catches this at <c>CliApplication.Create</c> time; the analyzer makes the diagnostic
/// appear in the IDE as soon as the second method is typed.
/// </summary>
/// <remarks>
/// Uses a compilation-wide action: each method contributes its route to a shared bag, then
/// at compilation-end we group by signature and report duplicates. Parallel-safe via
/// <see cref="ConcurrentBag{T}"/>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateRouteAnalyzer : DiagnosticAnalyzer
{
    private const string CliRouteAttributeFullName = "Portico.CliRouteAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(PorticoDiagnostics.DuplicateRoute);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var routes = new ConcurrentBag<RouteEntry>();

            start.RegisterSyntaxNodeAction(ctx => CollectRoute(ctx, routes), SyntaxKind.MethodDeclaration);

            start.RegisterCompilationEndAction(end => ReportDuplicates(end, routes));
        });
    }

    private static void CollectRoute(SyntaxNodeAnalysisContext context, ConcurrentBag<RouteEntry> routes)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.AttributeLists.Count == 0) return;

        foreach (var list in method.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var symbol = context.SemanticModel
                    .GetSymbolInfo(attr, context.CancellationToken).Symbol;
                if (symbol?.ContainingType?.ToDisplayString() != CliRouteAttributeFullName) continue;

                var firstArg = attr.ArgumentList?.Arguments.FirstOrDefault();
                if (firstArg?.Expression is not LiteralExpressionSyntax literal) continue;
                if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) continue;

                var raw = literal.Token.ValueText;
                var canonical = NormalizeRoute(raw);
                if (canonical.Length == 0) continue;

                var declaringSymbol = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken);
                var methodName = declaringSymbol is not null
                    ? $"{declaringSymbol.ContainingType?.ToDisplayString()}.{declaringSymbol.Name}"
                    : method.Identifier.ValueText;

                routes.Add(new RouteEntry(canonical, methodName, literal.GetLocation()));
            }
        }
    }

    private static void ReportDuplicates(CompilationAnalysisContext context, ConcurrentBag<RouteEntry> routes)
    {
        // Group by canonical signature, case-insensitively — the runtime dedups routes with
        // StringComparer.OrdinalIgnoreCase (CliApplication.Create), so "Commit" and "commit" collide
        // there; the analyzer must fire on the same pairs it would otherwise reject only at run time.
        var byRoute = new Dictionary<string, List<RouteEntry>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var entry in routes)
        {
            if (!byRoute.TryGetValue(entry.Canonical, out var list))
            {
                list = new List<RouteEntry>();
                byRoute[entry.Canonical] = list;
            }
            list.Add(entry);
        }

        foreach (var kv in byRoute)
        {
            var entries = kv.Value;
            if (entries.Count < 2) continue;

            // Report on each duplicate (entries[1..n]), citing entries[0] as the original.
            var first = entries[0];
            for (int i = 1; i < entries.Count; i++)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PorticoDiagnostics.DuplicateRoute,
                    entries[i].Location,
                    kv.Key,
                    first.MethodName,
                    entries[i].MethodName));
            }
        }
    }

    /// <summary>
    /// Collapse whitespace; preserve placeholder tokens. Case is preserved in the returned string
    /// (so the diagnostic message shows the route as written) but ignored when grouping duplicates,
    /// matching the runtime's <see cref="System.StringComparer.OrdinalIgnoreCase"/> dedup.
    /// </summary>
    private static string NormalizeRoute(string raw) =>
        string.Join(" ", raw.Split(' ', '\t').Where(s => s.Length > 0));

    private readonly struct RouteEntry
    {
        public readonly string Canonical;
        public readonly string MethodName;
        public readonly Location Location;
        public RouteEntry(string canonical, string methodName, Location location)
        {
            Canonical = canonical;
            MethodName = methodName;
            Location = location;
        }
    }
}

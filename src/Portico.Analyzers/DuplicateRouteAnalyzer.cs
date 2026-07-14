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
/// POR002 — two or more methods <b>on the same type</b> declare the same <c>[CliRoute]</c> signature
/// (after whitespace normalization). One type is one command surface, so a repeated route there is
/// unambiguously a mistake. The framework's runtime check at <c>CliApplication.Create</c> remains the
/// backstop; the analyzer makes the diagnostic appear as soon as the second method is typed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scoped to the declaring type on purpose (POR-52).</b> It used to group every route in the
/// compilation, which made it <em>stricter than the framework</em>: two contracts that each declare
/// <c>status</c> are a legal, working program when they are mounted under different root routes
/// (<c>AddCommands(tool, [new CliRouteAttribute("storage")])</c>) — the runtime dedups on the route
/// signature <em>after</em> the mount is prepended — or simply when the application registers only
/// one of them. The analyzer can see neither the mount nor the registration, so a cross-type
/// diagnostic failed builds that run correctly, and told the author to "add a subcommand prefix"
/// they had already added at the composition site.
/// </para>
/// <para>
/// An analyzer must never fail a build that works. The genuinely ambiguous case — two contracts with
/// the same route, both registered at the same mount — is still rejected at <c>CliApplication.Create</c>,
/// loudly, on startup, where the registration is actually visible.
/// </para>
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
                var declaringType = declaringSymbol?.ContainingType?.ToDisplayString() ?? "<unknown>";
                var methodName = declaringSymbol is not null
                    ? $"{declaringType}.{declaringSymbol.Name}"
                    : method.Identifier.ValueText;

                routes.Add(new RouteEntry(canonical, declaringType, methodName, literal.GetLocation()));
            }
        }
    }

    private static void ReportDuplicates(CompilationAnalysisContext context, ConcurrentBag<RouteEntry> routes)
    {
        // Group by DECLARING TYPE + canonical signature, case-insensitively on the route — the runtime
        // dedups with StringComparer.OrdinalIgnoreCase (CliApplication.Create), so "Commit" and
        // "commit" collide there and must collide here.
        //
        // The type is part of the key (POR-52): two types declaring the same route are a legal program
        // — they may be mounted under different root routes, or only one of them registered — and the
        // analyzer can see neither fact. See the remarks on this class.
        var byRoute = new Dictionary<(string Type, string Route), List<RouteEntry>>(RouteKeyComparer.Instance);
        foreach (var entry in routes)
        {
            var key = (entry.DeclaringType, entry.Canonical);
            if (!byRoute.TryGetValue(key, out var list))
            {
                list = new List<RouteEntry>();
                byRoute[key] = list;
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
                    kv.Key.Route,
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
        public readonly string DeclaringType;
        public readonly string MethodName;
        public readonly Location Location;
        public RouteEntry(string canonical, string declaringType, string methodName, Location location)
        {
            Canonical = canonical;
            DeclaringType = declaringType;
            MethodName = methodName;
            Location = location;
        }
    }

    /// <summary>
    /// Type name compared ordinally, route compared case-insensitively — the runtime's dedup is
    /// <see cref="System.StringComparer.OrdinalIgnoreCase"/> on the route, and C# type names are
    /// case-sensitive.
    /// </summary>
    private sealed class RouteKeyComparer : IEqualityComparer<(string Type, string Route)>
    {
        public static readonly RouteKeyComparer Instance = new();

        public bool Equals((string Type, string Route) x, (string Type, string Route) y) =>
            string.Equals(x.Type, y.Type, System.StringComparison.Ordinal) &&
            string.Equals(x.Route, y.Route, System.StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Type, string Route) obj) =>
            obj.Type.GetHashCode() ^ obj.Route.ToLowerInvariant().GetHashCode();
    }
}

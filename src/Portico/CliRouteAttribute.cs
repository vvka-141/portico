using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace Portico;

/// <summary>
/// Defines the route for a CLI command. Applied to a method, the signature is the full route
/// (after any class/interface-level prefix and the <c>AddCommands</c> root prefix). Applied to a class or interface, the signature is a <b>prefix</b> prepended to
/// every method-level route on that type — the declarative equivalent of passing rootRoutes to
/// <c>AddCommands</c>. When both the registered class and an inherited interface carry the
/// attribute, the class wins.
/// </summary>
/// <example><code>
/// public sealed class MyTool
/// {
///     [CliRoute("greet")]
///     public int Greet([CliOption("--name")] string name)
///     {
///         Console.WriteLine($"Hi {name}");
///         return 0;
///     }
/// }
/// </code></example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false)]
public sealed class CliRouteAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the CliCommandAttribute class.
    /// </summary>
    /// <param name="routeSignature">A space-separated string representing individual subcommands.</param>
    public CliRouteAttribute(string routeSignature)
    {
        RouteSignature = routeSignature;
        Segments = [
            ..Regex
                .Split(RouteSignature, @"(?<=\S)\s+(?=\S)")
                .Select(segment => segment.Trim())
        ];
    }

    /// <summary>
    /// Gets a space-separated string representing individual subcommands.
    /// </summary>
    public string RouteSignature { get; }

    /// <summary>The route's whitespace-split tokens (e.g. <c>["db", "migrate"]</c> for <c>"db migrate"</c>).</summary>
    public ImmutableArray<string> Segments { get; }

    /// <example><code>new CliRouteAttribute("db migrate").ToString() // "db migrate"</code></example>
    public override string ToString() => RouteSignature;
}
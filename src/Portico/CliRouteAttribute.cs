using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace Portico;

/// <summary>
/// Defines the route for a CLI command — the command's path in full, placeholders included. Applied
/// to a method, the signature is the route (after any class/interface-level prefix and the
/// <c>AddCommands</c> mount prefix). Applied to a class or interface, the signature is a
/// <b>prefix</b> prepended to every method-level route on that type. When both the registered class
/// and an inherited interface carry the attribute, the class wins.
/// </summary>
/// <remarks>
/// A <c>{name}</c> token binds to the method parameter of that name, from either level — a
/// type-level prefix decorates the same author's methods, so <c>[CliRoute("tenant {tenant}")]</c> on
/// an interface plus <c>[CliRoute("status")]</c> on its method routes <c>tenant acme status</c> and
/// binds <c>acme</c> to a <c>tenant</c> parameter on that method. A placeholder in a type-level
/// prefix therefore requires the parameter on <i>every</i> <c>[CliRoute]</c> method of the type.
/// <para>
/// The <c>AddCommands(x, rootRoutes)</c> mount prefix is <b>not</b> the same thing and takes literal
/// segments only — it is applied to commands declared elsewhere. See
/// <see cref="ICliApplicationBuilder.AddCommands(object, System.Collections.Generic.IEnumerable{CliRouteAttribute})"/>.
/// </para>
/// </remarks>
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
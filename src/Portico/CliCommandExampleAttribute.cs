using System;
using System.Collections.Generic;
using System.Reflection;

namespace Portico;

/// <summary>
/// Declares a worked example for the decorated CLI command. Examples are first-class
/// test cases — <see cref="Portico.Testing.CliContractValidator{T}"/>
/// runs each one through the framework against a <see cref="System.Reflection.DispatchProxy"/>
/// and fails the build if the example doesn't reach the proxy. They are also rendered
/// verbatim in <c>--help</c> output, so users can paste them and run them.
/// </summary>
/// <param name="example">
/// The command-line invocation as a user would type it, without the executable name —
/// e.g. <c>"to json data.csv --pretty"</c>. Must match the route + options declared on
/// the same method or contract validation fails.
/// </param>
/// <param name="description">
/// Optional one-line description of what the example demonstrates, surfaced beneath
/// the example in help output.
/// </param>
/// <example><code>
/// [CliRoute("greet")]
/// [CliCommandExample("greet --name Ada", "greet a user by name")]
/// public int Greet([CliOption("--name|-n")] string name) =&gt; 0;
/// </code></example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CliCommandExampleAttribute(string example, string description = "") : Attribute
{
    /// <summary>The verbatim example invocation, less the executable name.</summary>
    public string Example { get; } = example;

    /// <summary>Optional one-line description rendered beneath the example in help output.</summary>
    public string Description { get; } = description;

    /// <summary>Returns <see cref="Example"/>.</summary>
    /// <example><code>
    /// var attribute = new CliCommandExampleAttribute("greet --name Ada", "greet a user");
    /// string text = attribute.ToString(); // "greet --name Ada"
    /// </code></example>
    public override string ToString() => Example;
}
using System;

namespace Portico;

/// <summary>
/// Base class for a reusable group of <c>[CliOption]</c>-decorated properties. Declare
/// <c>class MyOptions : CliOptions</c>, expose properties with <c>[CliOption]</c>, then take a
/// single <c>MyOptions opts</c> parameter on any handler that needs those options — the
/// framework constructs a fresh instance per invocation and populates it from the command line.
/// </summary>
/// <remarks>
/// Parameterless ctor is the only requirement. For cross-cutting options + lifecycle hooks,
/// subclass <see cref="CliMiddleware"/> instead.
/// </remarks>
public abstract class CliOptions
{
    public static bool IsAssignableFrom(Type type) => typeof(CliOptions).IsAssignableFrom(type);
}

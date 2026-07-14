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
    // Internal, not public (POR-27): this is a one-line wrapper over a BCL call, its only callers are
    // the framework's own binder, and a user who wants it can write `typeof(CliOptions)
    // .IsAssignableFrom(t)` themselves. A public member nobody meant to ship is better removed than
    // documented.
    internal static bool IsAssignableFrom(Type type) => typeof(CliOptions).IsAssignableFrom(type);
}

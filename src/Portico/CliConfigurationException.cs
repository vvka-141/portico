using System;
using System.Diagnostics;

namespace Portico;

/// <summary>
/// Thrown by <see cref="CliApplication.Create"/> when the registered commands declare a
/// configuration the framework cannot honor — a malformed <c>[CliRoute]</c>, an unsupported
/// option type, a duplicate route across command handlers, etc. Distinct from
/// <see cref="CliExitException"/>, which signals user-input failures at runtime.
/// </summary>
public sealed class CliConfigurationException : Exception
{
    [DebuggerNonUserCode]
    public CliConfigurationException(string message) : base(message)
    {
    }

    // Used by CliOptionDefaultResolver to preserve the underlying conversion failure as
    // InnerException when a [CliOption] default value cannot be converted.
    [DebuggerNonUserCode]
    public CliConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    internal static CliConfigurationException DuplicateRoute(string routeSignature, string firstMethod, string secondMethod) =>
        new(
            $"Two CLI methods resolve to the same route signature '{routeSignature}': " +
            $"'{firstMethod}' and '{secondMethod}'. Each command route must be uniquely claimed by a single method. " +
            $"Rename one of the [CliRoute(\"...\")] attributes, or disambiguate via a service root route.");
}

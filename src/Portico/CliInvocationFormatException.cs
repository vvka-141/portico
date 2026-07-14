using System;

namespace Portico;

/// <summary>
/// Thrown when the command-line string cannot be parsed into a <see cref="CliInvocation"/> —
/// e.g. the executable name can't be extracted. Distinct from <see cref="CliExitException"/>
/// (user-visible exit) and <see cref="CliConfigurationException"/> (framework-setup error).
/// </summary>
public sealed class CliInvocationFormatException : FormatException
{
    private CliInvocationFormatException(string message) : base(message) { }

    private CliInvocationFormatException(string message, Exception innerException)
        : base(message, innerException) { }

    internal static CliInvocationFormatException GeneralFormatMismatch() => new(
        "The command line format is invalid. Expected format: [executable] [subcommands/arguments] [options].");

    internal static CliInvocationFormatException InvalidExecutableName(Exception innerException) => new(
        "Failed to extract executable name.", innerException);
}

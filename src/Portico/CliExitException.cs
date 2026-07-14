using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Portico;

/// <summary>
/// Exception that signals a controlled, user-visible exit from a CLI action.
/// The framework writes <see cref="Exception.Message"/> to the console and returns
/// <see cref="ExitCode"/>.
/// </summary>
[method: DebuggerNonUserCode]
public sealed class CliExitException(string message) : Exception(message)
{
    /// <summary>Successful execution. POSIX convention.</summary>
    public const int SuccessExitCode = 0;

    /// <summary>Runtime failure during action execution. POSIX convention.</summary>
    public const int RuntimeErrorExitCode = 1;

    /// <summary>User supplied an invalid or unrecognized command line. POSIX convention.</summary>
    public const int UsageErrorExitCode = 2;

    /// <summary>
    /// Process terminated by SIGINT (Ctrl+C). POSIX convention: 128 + signal number (2 for SIGINT).
    /// The framework maps <see cref="OperationCanceledException"/> to this code so shells and CI
    /// systems can distinguish user-initiated cancellation from other runtime failures.
    /// </summary>
    public const int CancelledExitCode = 130;

    /// <summary>
    /// Process terminated by SIGTERM. POSIX convention: 128 + signal number (15 for SIGTERM).
    /// SIGTERM is the signal Docker / Kubernetes send for graceful shutdown before escalating
    /// to SIGKILL. The framework maps SIGTERM-driven cancellation to this code so orchestrators
    /// and CI systems can distinguish a graceful pod-termination drain from a Ctrl+C
    /// (<see cref="CancelledExitCode"/>) or other runtime failure.
    /// </summary>
    public const int TerminatedExitCode = 143;

    /// <summary>The exit code the framework will return when this exception is caught.</summary>
    public int ExitCode { get; init; } = RuntimeErrorExitCode;

    internal static CliExitException AmbiguousCommand(IEnumerable<string> candidateSignatures)
    {
        var lines = candidateSignatures
            .Select(s => "  " + s)
            .ToArray();
        var message =
            "The command line matches more than one command. Candidates:" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "Disambiguate by supplying additional options or by using a more specific subcommand.";
        return new CliExitException(message) { ExitCode = UsageErrorExitCode };
    }
}

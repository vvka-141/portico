using System;
#if (async)
using System.Threading;
using System.Threading.Tasks;
#endif
using Portico;

namespace Portico.Generated;

/// <summary>
/// The implementation. Note what is not here: no base class, no framework types in the signature.
/// A handler is a plain method that writes with <c>Console.Write*</c> and returns an exit code.
/// </summary>
public sealed class PorticoCommandTool : IPorticoCommandTool
{
#if (async)
    public async Task<int> RunAsync(CliFlag? dryRun = null, CancellationToken cancellation = default)
    {
        if (dryRun is not null)
        {
            Console.WriteLine("dry run: nothing changed.");
            return 0;
        }

        await Task.CompletedTask;
        Console.WriteLine("TODO: do the work.");

        // The exit code IS the result. 0 = success; throw CliExitException for a failure path,
        // e.g. new CliExitException("...") { ExitCode = CliExitException.UsageErrorExitCode }.
        return 0;
    }
#else
    public int Run(CliFlag? dryRun = null)
    {
        if (dryRun is not null)
        {
            Console.WriteLine("dry run: nothing changed.");
            return 0;
        }

        Console.WriteLine("TODO: do the work.");

        // The exit code IS the result. 0 = success; throw CliExitException for a failure path,
        // e.g. new CliExitException("...") { ExitCode = CliExitException.UsageErrorExitCode }.
        return 0;
    }
#endif
}

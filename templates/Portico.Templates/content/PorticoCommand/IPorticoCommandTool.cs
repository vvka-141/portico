using System.ComponentModel;
#if (async)
using System.Threading;
using System.Threading.Tasks;
#endif
using Portico;

namespace Portico.Generated;

/// <summary>
/// This command's contract. The interface is not ceremony: it is what
/// <c>CliContractValidator&lt;IPorticoCommandTool&gt;</c> proxies, which is how the example below
/// becomes an executable test of your routing and binding.
/// </summary>
public interface IPorticoCommandTool
{
    /// <summary>Describe what this command does.</summary>
    // [Description] is what `yourcli --help` prints next to the command.
    [Description("TODO: what this command does")]
    [CliRoute("COMMAND_ROUTE")]
    // Not a comment. Run it through CliContractValidator<T> and the build goes red if it ever stops
    // dispatching. Delete it and the POR004 analyzer will ask for it back.
    [CliCommandExample("COMMAND_ROUTE")]
    [CliCommandExample("COMMAND_ROUTE --dry-run")]
#if (async)
    Task<int> RunAsync(
        // CliFlag? is presence-only: `--dry-run`, not `--dry-run true`. A bool would be a value option.
        [CliOption("--dry-run", "Print the plan; change nothing")] CliFlag? dryRun = null,
        // Declare a CancellationToken and the framework injects the ambient one — Ctrl+C and SIGTERM
        // cancel it, which is what turns `docker stop` into a drain rather than a kill.
        CancellationToken cancellation = default);
#else
    int Run(
        // CliFlag? is presence-only: `--dry-run`, not `--dry-run true`. A bool would be a value option.
        [CliOption("--dry-run", "Print the plan; change nothing")] CliFlag? dryRun = null);
#endif
}

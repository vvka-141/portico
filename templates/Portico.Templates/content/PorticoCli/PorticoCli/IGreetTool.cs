using System.ComponentModel;
using Portico;

namespace PorticoCli;

/// <summary>
/// Your CLI's contract. The interface is not ceremony: it is what
/// <c>CliContractValidator&lt;IGreetTool&gt;</c> proxies, which is how every example below becomes an
/// executable test of your routing and binding.
/// </summary>
public interface IGreetTool
{
    /// <summary>Greet someone.</summary>
    // [Description] is what `mycli --help` prints next to the command.
    [Description("Greet someone")]
    [CliRoute("greet")]
    // Not a comment. This runs through the real pipeline in PorticoCli.Tests, and the build goes red
    // if it ever stops dispatching. Delete it and the POR004 analyzer will ask for it back.
    [CliCommandExample("greet --name Ada")]
    [CliCommandExample("greet --name Grace --loud")]
    int Greet(
        [CliOption("--name|-n", "Who to greet")] string name,
        // CliFlag? is presence-only: `--loud`, not `--loud true`. A bool would be a value option.
        [CliOption("--loud", "Shout it")] CliFlag? loud = null);
}

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Portico;

namespace Platform.Queue;

/// <summary>
/// The queue team's CLI, built and shipped on its own. Note the <c>status</c> route: it collides,
/// verbatim, with the storage team's. Neither team had to know.
/// </summary>
public interface IQueueTool
{
    /// <summary>Report queue depth.</summary>
    [Description("Report queue depth")]
    [CliRoute("status")]
    [CliCommandExample("status")]
    [CliCommandExample("status --queue orders")]
    int Status([CliOption("--queue", "Limit the report to one queue")] string? queue = null);

    /// <summary>Drain in-flight messages and stop accepting new ones.</summary>
    [Description("Drain in-flight messages")]
    [CliRoute("drain")]
    [CliCommandExample("drain --timeout \"30 seconds\"")]
    Task<int> DrainAsync(
        [CliOption("--timeout", "How long to wait for in-flight messages")] System.TimeSpan? timeout = null,
        CancellationToken cancellation = default);
}

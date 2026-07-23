using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Portico;

namespace AdminCli;

/// <summary>
/// The operational surface of a backend service, declared as a contract.
///
/// Every route carries at least one <c>[CliCommandExample]</c>. Those examples are not comments:
/// <c>CliContractValidator&lt;IAdminTool&gt;</c> runs each one through the real pipeline in
/// AdminCli.Tests, and the build fails if any of them stops dispatching. The analyzer (POR004)
/// fails the build if a route ships without one at all.
/// </summary>
public interface IAdminTool
{
    /// <summary>Apply pending database migrations.</summary>
    // [Description] is what the Commands: listing in top-level help reads. Without it the route
    // still works — it just has nothing to say about itself when a user runs `admin --help`.
    [Description("Apply pending database migrations")]
    [CliRoute("db migrate")]
    [CliCommandExample("db migrate --connection-string \"Host=db;Username=svc\"")]
    [CliCommandExample("db migrate --connection-string \"Host=db\" --dry-run")]
    Task<int> MigrateAsync(
        // Sensitive: the value is redacted anywhere the framework echoes the command line —
        // trace output, timing output, conversion errors. A connection string must not reach a log.
        [CliOption("--connection-string|-c", "Postgres connection string", Sensitive = true)]
        string connectionString,
        [CliOption("--dry-run", "Print the plan; change nothing")] CliFlag? dryRun = null,
        CancellationToken cancellation = default);

    /// <summary>Seed reference data.</summary>
    [Description("Seed reference data")]
    [CliRoute("db seed")]
    [CliCommandExample("db seed --rows 100")]
    [CliCommandExample("db seed")]
    int Seed([CliOption("--rows", "How many rows to seed")] int rows = 10);

    /// <summary>Rebuild a search index. The index name is an optional positional.</summary>
    [Description("Rebuild a search index")]
    [CliRoute("reindex {index}")]
    [CliCommandExample("reindex")]
    [CliCommandExample("reindex orders")]
    [CliCommandExample("reindex orders --shard[eu] 3 --shard[us] 5")]
    int Reindex(
        // Optional trailing positional: omit it and 'all' binds.
        [CliArgument("which index to rebuild")] string index = "all",
        // Map option — the CLI analogue of ?shard[eu]=3 on a query string.
        [CliOption("--shard", "Per-region shard counts")] System.Collections.Generic.Dictionary<string, int>? shard = null);

    /// <summary>Drain in-flight work and stop accepting new work.</summary>
    [Description("Drain in-flight work and stop accepting new work")]
    [CliRoute("drain")]
    [CliCommandExample("drain --timeout \"30 seconds\"")]
    Task<int> DrainAsync(
        // A TimeSpan reads however an operator would type it: "30 seconds", "5 min", "1.5 hours",
        // "PT30S" or "00:00:30".
        [CliOption("--timeout", "How long to wait for in-flight work")] System.TimeSpan? timeout = null,
        CancellationToken cancellation = default);

    /// <summary>Report service health. Exit 0 = healthy, 1 = unhealthy — usable from a HEALTHCHECK.</summary>
    [Description("Report service health (exit 0 = healthy)")]
    [CliRoute("health")]
    [CliCommandExample("health")]
    int Health();
}

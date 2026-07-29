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

    /// <summary>
    /// Backfill a column for specific rows. This is the route
    /// <c>docs/how-to/operational-command.md</c> walks through — it carries one of each capability
    /// that earns the "backend services" claim, so the walkthrough can quote a command CI builds
    /// rather than a snippet nobody compiles.
    /// </summary>
    [Description("Backfill a column for specific rows")]
    [CliRoute("db backfill")]
    [CliCommandExample("db backfill --ids 41 42 43 --dry-run")]
    [CliCommandExample("db backfill --ids 41 42 43 --timeout \"5 min\"")]
    [CliCommandExample("db backfill")]
    Task<int> BackfillAsync(
        // EnvironmentVariable: an operator sets PGCONNSTR once in the container and stops typing it.
        // argv still wins when both are present. Sensitive keeps the value out of every message the
        // framework composes — and --help names the VARIABLE without ever reading it.
        [CliOption("--connection-string|-c", "Postgres connection string",
            EnvironmentVariable = "PGCONNSTR", Sensitive = true)]
        string? connectionString = null,
        // A collection option: `--ids 41 42 43` and `--ids 41 --ids 42 --ids 43` are the same thing.
        // Absent, it binds an EMPTY array rather than null.
        [CliOption("--ids", "Row ids to backfill (repeatable)")] int[]? ids = null,
        [CliOption("--dry-run", "Print the plan; change nothing")] CliFlag? dryRun = null,
        // "5 min", "90s", "1h30m", "PT5M" or "00:05:00" all bind. A bare "5" is refused, because to
        // .NET that means five DAYS.
        [CliOption("--timeout", "Give up after this long")] System.TimeSpan? timeout = null,
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

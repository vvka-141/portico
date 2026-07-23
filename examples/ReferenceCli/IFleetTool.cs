using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Portico;

namespace ReferenceCli;

/// <summary>
/// The operational surface of a worker-fleet service, declared as one contract. This is the
/// reference CLI: it exercises the full Portico surface on purpose, and every capability here has a
/// <b>reason</b> to exist on a fleet tool rather than being a feature dump.
///
/// The rules that make it a contract and not documentation:
/// <list type="bullet">
///   <item>Every route carries at least one <see cref="CliCommandExampleAttribute"/>. Those examples
///     run through the real pipeline in the test project and the build fails if any stops
///     dispatching. Analyzer POR004 fails the build if a route ships without one.</item>
///   <item>The route string is the command's whole path. A <c>{placeholder}</c> is a positional
///     argument; a <c>--flag</c> is an option; the mapping to method parameters is by name.</item>
/// </list>
/// </summary>
public interface IFleetTool
{
    /// <summary>
    /// List workers. Shows a <b>collection</b> option (repeat <c>--status</c>), an option with two
    /// <b>aliases</b> (<c>--verbose|-v</c>) typed as a presence-only <see cref="CliFlag"/>, and an
    /// <b>optional trailing positional</b> (<c>pool</c> defaults to <c>all</c>).
    /// </summary>
    [Description("List workers in a pool")]
    [CliRoute("worker list {pool}")]
    [CliCommandExample("worker list")]
    [CliCommandExample("worker list eu-west")]
    [CliCommandExample("worker list eu-west --status draining --status idle -v")]
    int ListWorkers(
        // Optional trailing positional: omit it and 'all' binds. Optionality is confined to the tail.
        [CliArgument("Which worker pool")] string pool = "all",
        // Collection option — repeat the flag to accumulate. Also accepts T[], IReadOnlyList<T>, etc.
        [CliOption("--status", "Filter by worker status (repeatable)")] List<string>? status = null,
        // CliFlag? is PRESENCE-ONLY: absent = off, `--verbose` or `-v` = on. It carries no value.
        // Contrast JobSpec.ParkOnFailure, a `bool`, which is two-state and needs `--park true`.
        [CliOption("--verbose|-v", "Show per-worker detail")] CliFlag? verbose = null);

    /// <summary>
    /// Drain one worker. Shows a <b>route placeholder</b> (<c>{id}</c>), a human-readable
    /// <see cref="TimeSpan"/> option, a two-state <c>bool</c>, and an auto-wired
    /// <see cref="CancellationToken"/>.
    /// </summary>
    [Description("Drain a worker and stop it accepting jobs")]
    [CliRoute("worker {id} drain")]
    [CliCommandExample("worker w-42 drain")]
    [CliCommandExample("worker w-42 drain --timeout \"2 minutes\" --force true")]
    Task<int> DrainWorkerAsync(
        // {id} in the route binds here by name.
        string id,
        // A TimeSpan reads the way an operator types it: "30 seconds", "2 minutes", "1.5 hours",
        // "PT2M" or "00:02:00".
        [CliOption("--timeout", "How long to wait for in-flight jobs")] TimeSpan? timeout = null,
        // A `bool` is TWO-STATE: `--force true` / `--force false`. Bare `--force` is an error — use
        // a CliFlag? (see --verbose above) when you want presence-only.
        [CliOption("--force", "Cancel in-flight jobs instead of waiting")] bool force = false,
        // Declare a CancellationToken parameter and the framework injects the ambient one — the same
        // token that trips to exit 130 on Ctrl+C when the app runs via RunAsync.
        CancellationToken cancellation = default);

    /// <summary>
    /// Submit a job. Shows a <see cref="CliOptions"/> <b>bundle</b> with cross-property
    /// <c>IValidatableObject</c> validation, and a <b>Sensitive</b> option whose value is redacted
    /// anywhere the framework echoes the command line.
    /// </summary>
    [Description("Submit a job to a queue")]
    [CliRoute("job submit")]
    [CliCommandExample("job submit --queue orders --priority 7")]
    [CliCommandExample("job submit --queue orders --park-on-failure true --max-retries 3")]
    int SubmitJob(
        // The bundle binds --queue/--priority/--max-retries/--park-on-failure and self-validates.
        JobSpec spec,
        // Sensitive: the value is replaced with *** in trace/timing output and conversion errors.
        // A credential must never reach a log line, and a mistyped command must not print it either.
        [CliOption("--auth-token", "Submission credential", Sensitive = true)] string? authToken = null);

    /// <summary>
    /// Tail a job's logs. Shows a placeholder and a string-form <see cref="CliOptionAttribute.DefaultValue"/>
    /// on a parameter that has no C# default.
    /// </summary>
    [Description("Tail a job's logs")]
    [CliRoute("job {id} logs")]
    [CliCommandExample("job j-100 logs")]
    [CliCommandExample("job j-100 logs --tail 500")]
    int JobLogs(
        string id,
        // DefaultValue is the STRING form of a default, parsed through the same converter a typed
        // value would be — useful when the parameter has no C# default. Distinct from `int tail = 100`.
        [CliOption("--tail", "How many trailing lines", DefaultValue = "100")] int tail);

    /// <summary>
    /// Set per-topic retention. Shows a <b>map</b> option — the CLI analogue of a
    /// <c>?keep[orders]=7</c> query string — binding to a <see cref="Dictionary{TKey,TValue}"/>.
    /// </summary>
    [Description("Set per-topic message retention (days)")]
    [CliRoute("queue retain")]
    [CliCommandExample("queue retain --keep[orders] 7 --keep[audit] 90")]
    int SetRetention(
        // Map option: --keep[key] value repeats to fill the dictionary.
        [CliOption("--keep", "Retention days per topic")] Dictionary<string, int>? keep = null);

    /// <summary>
    /// Ping the cluster. Shows an <b>environment-variable fallback</b>: the endpoint comes from
    /// <c>FLEET_ENDPOINT</c> when the flag is absent — config layering for a containerized service,
    /// without a config file.
    /// </summary>
    [Description("Check connectivity to the cluster endpoint")]
    [CliRoute("cluster ping")]
    [CliCommandExample("cluster ping")]
    [CliCommandExample("cluster ping --endpoint https://fleet.internal:8443")]
    int Ping(
        // The command line wins over the environment; the environment beats the C# default.
        [CliOption("--endpoint", "Cluster endpoint", EnvironmentVariable = "FLEET_ENDPOINT")]
        string endpoint = "http://localhost:8080");

    /// <summary>
    /// Run an arbitrary maintenance command against the fleet — a passthrough catch-all. Paired with
    /// <see cref="Migrate"/> below to demonstrate <c>RankByOptions</c>: two routes of the same
    /// segment shape, disambiguated by which options are present.
    /// </summary>
    [Description("Forward a maintenance command to every worker")]
    [CliRoute("run {command}")]
    [CliCommandExample("run gc")]
    int Run(string command);

    /// <summary>
    /// Migrate the fleet's on-disk schema — a specialized form of <c>run {command}</c>. When the
    /// user types <c>run migrate --to v5</c>, both this route and the <c>run {command}</c> catch-all
    /// match the two segments <c>run migrate</c>; the required <c>--to</c> option ranks this one
    /// ahead (+1 for a matched option here, −1 for an unrecognized one on the catch-all). Typing
    /// <c>run migrate</c> with no <c>--to</c> falls through to the passthrough by design — the
    /// specialized route needs its distinguishing option to win.
    /// </summary>
    [Description("Migrate the fleet's on-disk schema")]
    [CliRoute("run migrate")]
    [CliCommandExample("run migrate --to v5")]
    int Migrate([CliOption("--to", "Target schema version")] string to);
}

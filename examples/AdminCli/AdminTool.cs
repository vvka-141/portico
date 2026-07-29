using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Portico;

namespace AdminCli;

/// <summary>
/// The implementation. Note what is NOT here: no plumbing, no base class, no framework types in the
/// signatures. A handler is a plain C# method that writes with <c>Console.Write*</c> and returns an
/// exit code (or throws <see cref="CliExitException"/>). That is the whole contract.
/// </summary>
public sealed class AdminTool : IAdminTool
{
    public async Task<int> MigrateAsync(string connectionString, CliFlag? dryRun, CancellationToken cancellation)
    {
        if (dryRun is not null)
        {
            Console.WriteLine("dry run: 3 migrations pending, nothing applied.");
            return 0;
        }

        await Task.Delay(10, cancellation);
        Console.WriteLine("applied 3 migrations.");
        return 0;
    }

    public async Task<int> BackfillAsync(
        string? connectionString,
        int[]? ids,
        CliFlag? dryRun,
        TimeSpan? timeout,
        CancellationToken cancellation)
    {
        // Neither argv nor PGCONNSTR supplied one. A named exit code, not a bare literal — an
        // operator reading `echo $?` in a pipeline gets 2 (usage), which is what "you configured
        // this wrong" means, distinct from 1 (the run failed).
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new CliExitException(
                "No connection string. Pass --connection-string, or set PGCONNSTR.")
            {
                ExitCode = CliExitException.UsageErrorExitCode,
            };
        }

        // `ids ?? []` rather than a bare `foreach`: the framework binds an empty array when --ids is
        // absent, so this cannot throw at run time — but the `?` that makes the option optional is
        // also what makes the compiler ask. Honest version of the POR-150 guarantee: no
        // NullReferenceException, and nullable reference types still want the acknowledgement.
        var rows = ids ?? [];
        if (rows.Length == 0)
        {
            Console.WriteLine("no ids given; nothing to backfill.");
            return 0;
        }

        var budget = timeout ?? TimeSpan.FromMinutes(1);

        if (dryRun is not null)
        {
            Console.WriteLine($"dry run: would backfill {rows.Length} row(s) within {budget.TotalSeconds:0}s.");
            return 0;
        }

        // The token is cancelled by Ctrl+C and by SIGTERM. Honouring it is what turns `docker stop`
        // into a drain instead of a kill — the framework maps the signal to an exit code, but only
        // the handler can decide what "finish cleanly" means.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(budget);

        foreach (var id in rows)
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Delay(1, deadline.Token);
            Console.WriteLine($"backfilled row {id}.");
        }

        Console.WriteLine($"backfilled {rows.Length} row(s).");
        return 0;
    }

    public int Seed(int rows)
    {
        Console.WriteLine($"seeded {rows} rows.");
        return 0;
    }

    public int Reindex(string index, Dictionary<string, int>? shard)
    {
        Console.WriteLine($"reindexing '{index}'.");
        foreach (var (region, count) in shard ?? [])
        {
            Console.WriteLine($"  {region}: {count} shards");
        }
        return 0;
    }

    public async Task<int> DrainAsync(TimeSpan? timeout, CancellationToken cancellation)
    {
        var budget = timeout ?? TimeSpan.FromSeconds(15);
        Console.WriteLine($"draining, up to {budget.TotalSeconds:0}s...");
        await Task.Delay(10, cancellation);
        Console.WriteLine("drained.");
        return 0;
    }

    public int Health()
    {
        var healthy = true;
        Console.WriteLine(healthy ? "healthy" : "unhealthy");

        // A non-zero exit is how a container HEALTHCHECK reads the answer.
        return healthy ? 0 : CliExitException.RuntimeErrorExitCode;
    }
}

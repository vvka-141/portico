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

    public async Task<int> DrainAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        var budget = timeout == TimeSpan.Zero ? TimeSpan.FromSeconds(15) : timeout;
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

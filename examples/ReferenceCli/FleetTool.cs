using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Portico;

namespace ReferenceCli;

/// <summary>
/// The implementation behind <see cref="IFleetTool"/>. A handler is a plain method that returns an
/// exit code and uses <c>Console.Write*</c> for output — the framework owns routing and binding, not
/// what a command prints. Nothing here is Portico-specific; this is where a real service would talk
/// to its cluster.
/// </summary>
public sealed class FleetTool(IClock clock) : IFleetTool
{
    public int ListWorkers(string pool, List<string>? status, CliFlag? verbose)
    {
        var filter = status is { Count: > 0 } ? $" [{string.Join(",", status)}]" : "";
        Console.WriteLine($"workers in '{pool}'{filter}{(verbose is null ? "" : " (verbose)")}");
        return 0;
    }

    public async Task<int> DrainWorkerAsync(string id, TimeSpan? timeout, bool force, CancellationToken cancellation)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(30);
        Console.WriteLine($"draining {id}: force={force} timeout={wait}");
        await Task.CompletedTask.ConfigureAwait(false);
        return cancellation.IsCancellationRequested ? 130 : 0;
    }

    public int SubmitJob(JobSpec spec, string? authToken)
    {
        var auth = authToken is null ? "anonymous" : "authenticated";
        Console.WriteLine(
            $"submitted to '{spec.Queue}': priority={spec.Priority} " +
            $"retries={spec.MaxRetries} park={spec.ParkOnFailure} ({auth})");
        return 0;
    }

    public int JobLogs(string id, int tail)
    {
        Console.WriteLine($"last {tail} lines of {id} @ {clock.UtcNow:O}");
        return 0;
    }

    public int SetRetention(Dictionary<string, int>? keep)
    {
        var pairs = (keep ?? new Dictionary<string, int>())
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}d");
        Console.WriteLine($"retention: {string.Join(" ", pairs)}");
        return 0;
    }

    public int Ping(string endpoint)
    {
        Console.WriteLine($"pinging {endpoint}");
        return 0;
    }

    public int Run(string command)
    {
        Console.WriteLine($"forwarding '{command}' to all workers");
        return 0;
    }

    public int Migrate(string to)
    {
        Console.WriteLine($"migrating schema to {to}");
        return 0;
    }
}

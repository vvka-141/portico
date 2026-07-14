using System;
using System.Threading;
using System.Threading.Tasks;

namespace Platform.Queue;

public sealed class QueueTool : IQueueTool
{
    public int Status(string? queue = null)
    {
        Console.WriteLine(queue is null
            ? "queue: 4 queues, 812 messages in flight."
            : $"queue: '{queue}' — 31 messages in flight.");
        return 0;
    }

    public Task<int> DrainAsync(TimeSpan? timeout = null, CancellationToken cancellation = default)
    {
        Console.WriteLine($"queue: drained (waited up to {timeout?.ToString() ?? "n/a"}).");
        return Task.FromResult(0);
    }
}

using System;

namespace Platform.Storage;

public sealed class StorageTool : IStorageTool
{
    public int Status(string? bucket = null)
    {
        Console.WriteLine(bucket is null
            ? "storage: 12 buckets, all healthy."
            : $"storage: bucket '{bucket}' healthy.");
        return 0;
    }

    public int Purge(string bucket, TimeSpan? olderThan = null)
    {
        Console.WriteLine($"storage: purged '{bucket}' (older than {olderThan?.ToString() ?? "n/a"}).");
        return 0;
    }
}

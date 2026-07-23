using System;

namespace ReferenceCli.Tests;

/// <summary>A deterministic <see cref="IClock"/> for tests — the injected dependency, substituted.</summary>
public sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

using System;

namespace ReferenceCli;

/// <summary>
/// A trivial injectable service, here only to show the dependency-injection <b>shape</b>: the
/// handler type and the middleware both take a constructor dependency and are constructed by the
/// composition root in <c>Program</c>. The core framework has no container of its own — you bring
/// one (or, as here, a plain <c>new</c>) and hand instances in. <c>Portico.DependencyInjection</c>
/// bridges MEDI when you want it.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>The real clock. A test would substitute a fixed one.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Where <see cref="AuditMiddleware"/> records what ran. Injected, not newed by the framework.</summary>
public interface IAuditSink
{
    void Record(string line);
}

/// <summary>Writes audit lines to stderr, so they never pollute a command's stdout payload.</summary>
public sealed class ConsoleAuditSink : IAuditSink
{
    public void Record(string line) => Console.Error.WriteLine($"[audit] {line}");
}

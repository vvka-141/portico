using System;
using System.Diagnostics;

namespace Portico;

/// <summary>
/// Ready-made middleware that times every command invocation and prints the elapsed
/// wall-clock duration to stderr when the user passes <c>--timing</c>. Register with
/// <c>cfg.UseMiddleware(new CliTimingMiddleware())</c>.
/// </summary>
/// <remarks>
/// Opt-in by design — without <c>--timing</c>, the middleware is silent, adding only a
/// <see cref="Stopwatch"/> start/stop on the dispatch path. The timing line goes to
/// <see cref="Console.Error"/> so it doesn't pollute stdout capture in pipelines
/// (<c>mytool do | jq</c> keeps working). Format:
/// <code>
/// [timing] deploy prod ... 182 ms
/// </code>
/// For richer output (structured logs, OpenTelemetry, custom format), subclass
/// <see cref="CliMiddleware"/> directly rather than extending this one.
/// </remarks>
public sealed class CliTimingMiddleware : CliMiddleware
{
    /// <summary>Presence-only switch. When set, the timing line is emitted.</summary>
    [CliOption("--timing", "Print per-command wall-clock timing to stderr")]
    public CliFlag? Timing { get; set; }

    private Stopwatch? _stopwatch;

    public override void OnExecutingAction(CliInvocation invocation)
    {
        _stopwatch = Stopwatch.StartNew();
        base.OnExecutingAction(invocation);
    }

    public override void OnActionExecuted(CliInvocation invocation)
    {
        _stopwatch?.Stop();
        if (Timing.HasValue && _stopwatch is not null)
        {
            Console.Error.WriteLine($"[timing] {invocation} ... {_stopwatch.ElapsedMilliseconds} ms");
        }
        base.OnActionExecuted(invocation);
    }
}

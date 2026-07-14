using System.Diagnostics;

namespace Portico;

/// <summary>
/// A ready-made middleware that bridges <see cref="System.Diagnostics.Trace"/> output into the
/// application's CLI console for the duration of a command, gated by a <c>--trace-level</c> option.
/// Register it to let library code that already writes to <see cref="Trace"/> surface diagnostics
/// through your CLI without changing that code.
/// </summary>
/// <remarks>
/// <para><strong>Concurrency caveat.</strong> <see cref="TraceListener"/> registration is
/// <em>process-global</em> (<see cref="TraceListenerCollection"/> on <see cref="Trace.Listeners"/>).
/// This middleware adds only its own listener on <see cref="OnExecutingAction"/> and removes exactly
/// that one on <see cref="OnActionExecuted"/>, so it never clobbers listeners registered by host code
/// or other middleware. But because <see cref="Trace"/> itself is global, two <em>concurrent</em>
/// invocations in the same process will each receive the other's trace output for the overlapping
/// window. Treat it as a single-writer affordance per process; for parallel dispatch, prefer an
/// explicit <see cref="System.Diagnostics.TraceSource"/> instead of ambient
/// <see cref="Trace"/>.</para>
/// </remarks>
/// <example><code>
/// var app = CliApplication.Create(cfg =&gt; cfg
///     .UseMiddleware(new CliTracingMiddleware())
///     .AddCommands(new MyTool()));
/// // `mytool work --trace-level Verbose` now routes Trace.* output to the console.
/// </code></example>
[DebuggerDisplay("CliTracing: {Level}")]
public sealed class CliTracingMiddleware : CliMiddleware
{
    // Track the listener WE added so cleanup removes only our own — not whatever else may
    // have been registered before/during the action (other middleware, host code, parallel
    // invocations). Trace.Listeners is process-global; clobbering it with Clear() loses
    // anyone else's listeners.
    private TraceListener? _ownListener;

    /// <summary>The minimum <see cref="TraceLevel"/> to surface. Defaults to <see cref="TraceLevel.Off"/>.</summary>
    [CliOption("--trace-level|--trace", "Trace level", DefaultValue = "Off")]
    public TraceLevel Level { get; set; } = TraceLevel.Off;

    public override void OnExecutingAction(CliInvocation invocation)
    {
        // Route through the injected console (falls back to the process console only if the
        // middleware is driven outside a dispatch) so output honours redirection + testing.
        var writer = (AttachedConsole ?? SystemCliConsole.Instance).Out;
        _ownListener = new CliTraceListener(Level, writer);
        Trace.Listeners.Add(_ownListener);
        base.OnExecutingAction(invocation);
    }

    public override void OnActionExecuted(CliInvocation invocation)
    {
        if (_ownListener is not null)
        {
            Trace.Listeners.Remove(_ownListener);
            _ownListener = null;
        }
        base.OnActionExecuted(invocation);
    }

}

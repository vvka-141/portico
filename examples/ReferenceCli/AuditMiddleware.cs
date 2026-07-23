using System;
using Portico;

namespace ReferenceCli;

/// <summary>
/// Cross-cutting audit logging — the CLI analogue of an ASP.NET <c>IActionFilter</c>. It exposes a
/// global <c>--audit</c> option that works on every route, and records each invocation through an
/// <b>injected</b> <see cref="IAuditSink"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Middleware may take constructor dependencies.</b> You construct it yourself and hand the
/// instance to <c>UseMiddleware(...)</c> — the framework never calls <c>Activator.CreateInstance</c>
/// on middleware — so injecting a service is the ordinary shape:
/// <c>UseMiddleware(new AuditMiddleware(sink))</c>, or from a container,
/// <c>UseMiddleware(sp.GetRequiredService&lt;AuditMiddleware&gt;())</c>. (This is the difference from
/// a <see cref="CliOptions"/> bundle, which IS constructed per invocation and needs a public
/// parameterless constructor — analyzer rule POR006.)
/// </para>
/// <para>
/// Values of <c>Sensitive</c> options are already redacted in
/// <see cref="CliInvocation.ToString()"/>, so logging the invocation is safe.
/// </para>
/// </remarks>
public sealed class AuditMiddleware(IAuditSink sink) : CliMiddleware
{
    /// <summary>Enable auditing for this run. A global option — it works on every route.</summary>
    [CliOption("--audit", "Record this invocation to the audit sink")]
    public CliFlag? Audit { get; set; }

    /// <summary>Runs before the handler. The <c>Sensitive</c> redaction is already applied here.</summary>
    public override void OnExecutingAction(CliInvocation invocation)
    {
        if (Audit is not null)
        {
            sink.Record($"start: {invocation}");
        }
    }

    /// <summary>
    /// Runs after the handler, whether it succeeded or threw. With several middlewares this half of
    /// the pipeline unwinds in reverse registration order, so teardown nests.
    /// </summary>
    public override void OnActionExecuted(CliInvocation invocation)
    {
        if (Audit is not null)
        {
            sink.Record($"done:  {invocation.ExecutableName}");
        }
    }
}

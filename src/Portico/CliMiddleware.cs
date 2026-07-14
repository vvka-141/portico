using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using Portico.Reflection;

namespace Portico;

/// <summary>
/// Cross-cutting options + lifecycle hooks that wrap every matched command — the CLI
/// equivalent of ASP.NET Core middleware. Expose <c>[CliOption]</c> properties for
/// application-wide options (e.g. <c>--verbose</c>, <c>--trace-level</c>), then override
/// <see cref="OnExecutingAction"/> / <see cref="OnActionExecuted"/> / <see cref="OnError"/>
/// to run code before / after / around every invocation. Register once via
/// <c>ICliApplicationBuilder.UseMiddleware(new MyMiddleware())</c>.
/// </summary>
/// <remarks>
/// <para>
/// A fresh clone is populated from the parsed invocation on every <see cref="CliApplication.Run(string)"/>
/// call, so state set in <see cref="OnExecutingAction"/> is thread-local per dispatch.
/// </para>
/// <para>
/// <b>Constructor dependencies are supported.</b> You construct the middleware yourself and hand
/// the instance to <c>UseMiddleware(...)</c> — the framework never calls
/// <c>Activator.CreateInstance</c> on it — so injecting a service is the ordinary shape:
/// <c>UseMiddleware(serviceProvider.GetRequiredService&lt;AuditMiddleware&gt;())</c>. (This is the
/// difference between middleware and a <see cref="CliOptions"/> <i>bundle</i>, which IS
/// Activator-constructed per invocation and therefore does need a public parameterless ctor —
/// analyzer rule POR006.)
/// </para>
/// <para>
/// <b>Caveat, because the per-dispatch copy is shallow.</b> <see cref="Clone"/> is
/// <c>MemberwiseClone</c>, so a reference-typed field is <i>shared</i> across clones rather than
/// duplicated. That is exactly right for an injected, stateless service. It is wrong for mutable
/// per-invocation state: keep that in a field the hooks assign during the dispatch, not in a shared
/// object handed in through the constructor.
/// </para>
/// </remarks>
public abstract class CliMiddleware : CliOptions, ICloneable
{
    private readonly ImmutableArray<CliOptionsPropertyInfo> _options;


    public CliMiddleware()
    {
        _options =
            [
                ..GetType()
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .SelectMany(pi =>
                    {
                        var attributes = pi.GetCustomAttributes(true);
                        if (attributes.OfType<CliOptionAttribute>().Any())
                        {
                            return new[] { new CliOptionsPropertyInfo(pi) };
                        }

                        return [];
                    })
            ];
    }


    /// <summary>
    /// Runs after the route matched and this middleware's own options were bound, but before the
    /// handler is invoked — the CLI's <c>OnActionExecuting</c>. Throw <see cref="CliExitException"/>
    /// here to reject the invocation and choose its exit code; the handler then never runs.
    /// </summary>
    /// <param name="invocation">
    /// The matched invocation. Values of <c>Sensitive</c> options are already redacted in its
    /// <see cref="CliInvocation.ToString()"/> — it is safe to log.
    /// </param>
    /// <example><code>
    /// public sealed class TraceMiddleware : CliMiddleware
    /// {
    ///     [CliOption("--verbose")] public bool Verbose { get; set; }
    ///     public override void OnExecutingAction(CliInvocation invocation)
    ///     {
    ///         if (Verbose) Console.WriteLine($"&gt; {invocation}");
    ///     }
    /// }
    /// </code></example>
    public virtual void OnExecutingAction(CliInvocation invocation)
    {
        Debug.WriteLine($"{GetType()}.{nameof(OnExecutingAction)}");
    }

    /// <summary>
    /// Runs after the dispatch, <b>whether it succeeded or threw</b> — it is invoked from a
    /// <c>finally</c>, so it is the symmetric counterpart to <see cref="OnExecutingAction"/> and the
    /// right place to release anything that hook acquired. It runs even if
    /// <see cref="OnExecutingAction"/> itself threw partway through. A failure additionally reaches
    /// <see cref="OnError"/>, which runs first; this hook is not told the outcome.
    /// </summary>
    /// <param name="invocation">The invocation that just completed, successfully or not.</param>
    /// <example><code>
    /// public override void OnActionExecuted(CliInvocation invocation)
    /// {
    ///     Console.WriteLine($"Completed: {invocation.ExecutableName}");
    /// }
    /// </code></example>
    public virtual void OnActionExecuted(CliInvocation invocation)
    {
        Debug.WriteLine($"{GetType()}.{nameof(OnActionExecuted)}");
    }

    /// <summary>
    /// Runs when the dispatch failed, before <see cref="OnActionExecuted"/>. Observational only: the
    /// original exception propagates regardless, and an exception thrown from this hook is swallowed
    /// — a buggy telemetry hook must not mask the failure the user actually needs to see. To change
    /// the exit code, throw <see cref="CliExitException"/> from
    /// <see cref="OnExecutingAction"/> instead.
    /// </summary>
    /// <param name="invocation">The invocation that failed.</param>
    /// <param name="exception">
    /// The failure. A handler's exception arrives unwrapped (not as a <c>TargetInvocationException</c>).
    /// </param>
    /// <example><code>
    /// public override void OnError(CliInvocation invocation, Exception exception)
    /// {
    ///     Console.Error.WriteLine($"Failed: {exception.Message}");
    /// }
    /// </code></example>
    public virtual void OnError(CliInvocation invocation, Exception exception)
    {
        Debug.WriteLine($"{GetType()}.{nameof(OnError)}");
    }

    /// <summary>
    /// Produces the per-dispatch copy the framework binds options onto, so one registered middleware
    /// instance can serve concurrent invocations without them writing over each other's option
    /// values. Called by the framework; you rarely call it yourself.
    /// <b>The copy is shallow</b> (<c>MemberwiseClone</c>): a reference-typed field — an injected
    /// service, say — is shared across clones, not duplicated. That is right for a stateless
    /// dependency and wrong for mutable per-invocation state.
    /// </summary>
    /// <returns>A shallow copy of this middleware.</returns>
    /// <example><code>var perDispatch = (CliMiddleware)sharedMiddleware.Clone();</code></example>
    public CliMiddleware Clone() => (CliMiddleware)this.MemberwiseClone();

    object ICloneable.Clone() => Clone();

    /// <summary>
    /// The application's <see cref="ICliConsole"/>, injected by the framework onto the per-dispatch
    /// clone before the lifecycle hooks run. Middleware that emits output should write here so it
    /// honours redirection and testing, rather than to the process-global <see cref="System.Console"/>.
    /// Named to avoid shadowing <c>System.Console</c> in subclasses.
    /// </summary>
    private protected ICliConsole? AttachedConsole { get; private set; }

    internal void AttachConsole(ICliConsole console) => AttachedConsole = console;

    internal IEnumerable<ICliOptionMemberInfo> GetOptions()
    {
        foreach (var option in _options)
        {
            yield return option;
        }
    }

    internal void PopulateFrom(CliInvocation invocation)
    {
        foreach (var info in _options)
        {
            var value = info.Materialize(invocation);
            if (value != null)
            {
                info.SetValue(this, value);
            }
        }
    }
}

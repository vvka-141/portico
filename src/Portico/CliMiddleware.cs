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

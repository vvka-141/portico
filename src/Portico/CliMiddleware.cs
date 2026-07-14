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
/// A fresh clone is populated from the parsed invocation on every <see cref="CliApplication.Run(string)"/>
/// call, so state set in <see cref="OnExecutingAction"/> is thread-local per dispatch.
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

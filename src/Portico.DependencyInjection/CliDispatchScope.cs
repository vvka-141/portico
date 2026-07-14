using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Portico.DependencyInjection;

/// <summary>
/// The per-dispatch <see cref="IServiceScope"/> a resolved command is built from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an <see cref="AsyncLocal{T}"/> and not a field.</b> The framework calls the command's
/// factory <i>before</i> it runs any middleware (<c>CliAction.InvokeAsync</c> evaluates
/// <c>instanceFactory()</c> as the argument to the invoker). So the scope cannot be opened by a
/// middleware hook — it must be opened by the factory, on the dispatching flow, and closed after the
/// handler returns. The factory and the closing hook have no reference to each other; the flow is
/// what they share.
/// </para>
/// <para>
/// One command per process is the normal case, but a test harness dispatches many, so the scope is
/// per-flow rather than per-process. Concurrent invocations each get their own.
/// </para>
/// </remarks>
internal static class CliDispatchScope
{
    private static readonly AsyncLocal<IServiceScope?> Current = new();

    /// <summary>
    /// Resolves <typeparamref name="T"/> from the current dispatch's scope, opening one on first use.
    /// Two commands resolved during one dispatch share the scope — which is the point of a scope.
    /// </summary>
    public static T Resolve<T>(IServiceProvider services) where T : class
    {
        var scope = Current.Value;
        if (scope is null)
        {
            scope = services.GetRequiredService<IServiceScopeFactory>().CreateScope();
            Current.Value = scope;
        }

        return scope.ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Disposes the current dispatch's scope, if one was opened. Idempotent: registering the closing
    /// middleware twice (two <c>AddCommands</c> calls against the same provider) must not double-dispose.
    /// </summary>
    public static void Close()
    {
        var scope = Current.Value;
        if (scope is null) return;

        Current.Value = null;
        try
        {
            scope.Dispose();
        }
        catch (Exception e)
        {
            // The command's exit code is the user's answer; a container's disposal fault must not
            // replace it. Same discipline the core applies to its own invocation-scope cleanup.
            Debug.WriteLine($"Portico: dispatch scope disposal failed: {e}");
        }
    }
}

/// <summary>
/// Closes the dispatch scope opened by the command factory. Registered automatically by
/// <see cref="CliApplicationBuilderExtensions.AddCommands{T}(ICliApplicationBuilder, IServiceProvider)"/>
/// — a user never sees it.
/// </summary>
/// <remarks>
/// <see cref="CliMiddleware.OnActionExecuted"/> is invoked from the invoker's <c>finally</c>, so the
/// scope is disposed whether the command succeeded, threw, or was cancelled. That symmetry is the
/// reason this is a middleware and not a line at the end of the happy path.
/// </remarks>
internal sealed class CliServiceScopeMiddleware : CliMiddleware
{
    /// <inheritdoc/>
    /// <example><code>
    /// // Registered for you by AddCommands&lt;T&gt;(serviceProvider); not part of the user's surface.
    /// </code></example>
    public override void OnActionExecuted(CliInvocation invocation)
    {
        CliDispatchScope.Close();
        base.OnActionExecuted(invocation);
    }
}

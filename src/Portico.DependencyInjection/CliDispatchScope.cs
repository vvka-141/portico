using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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
    private sealed class DispatchScope(AsyncServiceScope scope)
    {
        public AsyncServiceScope Scope { get; } = scope;
    }

    private static readonly AsyncLocal<DispatchScope?> Current = new();

    /// <summary>
    /// Resolves <paramref name="contractType"/> from the current dispatch's scope, opening one on
    /// first use. Two commands resolved during one dispatch share the scope — which is the point of
    /// a scope.
    /// </summary>
    public static object Resolve(Type contractType, IServiceProvider services)
    {
        var dispatchScope = Current.Value;
        if (dispatchScope is null)
        {
            dispatchScope = new DispatchScope(services.CreateAsyncScope());
            Current.Value = dispatchScope;
        }

        return dispatchScope.Scope.ServiceProvider.GetRequiredService(contractType);
    }

    /// <summary>
    /// Asynchronously disposes the current dispatch's scope, if one was opened. Idempotent: multiple
    /// registered command contracts must not double-dispose the shared per-dispatch scope.
    /// </summary>
    public static async ValueTask CloseAsync()
    {
        var dispatchScope = Current.Value;
        if (dispatchScope is null) return;

        Current.Value = null;
        try
        {
            await dispatchScope.Scope.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // The command's exit code is the user's answer; a container's disposal fault must not
            // replace it. Same discipline the core applies to its own invocation-scope cleanup.
            Debug.WriteLine($"Portico: dispatch scope disposal failed: {e}");
        }
    }

    /// <summary>
    /// Compatibility cleanup for custom <see cref="ICliApplicationBuilder"/> implementations that
    /// cannot use the core's internal asynchronous lifetime seam.
    /// </summary>
    public static void Close() => CloseAsync().AsTask().GetAwaiter().GetResult();
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

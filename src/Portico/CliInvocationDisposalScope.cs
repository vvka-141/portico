using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Portico;

/// <summary>
/// A per-invocation registry of <see cref="IDisposable"/> resources created while binding a
/// command's arguments — most notably the timeout-bound <see cref="CancellationTokenSource"/>
/// produced by <see cref="CliCancellationTokenTypeConverter"/>, whose <c>Token</c> is handed
/// to the handler but whose own lifetime would otherwise be orphaned (a scheduled timer that
/// lives until the timeout elapses). The scope is opened by
/// <see cref="Reflection.CliMethodInfo.InvokeAsync"/> before argument materialization and
/// disposed when the invocation completes — success, error, or cancellation — releasing every
/// registered resource regardless of how early the handler returned.
/// </summary>
/// <remarks>
/// Materialization runs synchronously inside <c>InvokeAsync</c> before the first <c>await</c>,
/// so an <see cref="AsyncLocal{T}"/> set at scope start is visible to the converter on the same
/// flow. Concurrent invocations (e.g. a test harness running many in one process) each get an
/// isolated scope.
/// </remarks>
internal static class CliInvocationDisposalScope
{
    private static readonly AsyncLocal<List<IDisposable>?> Current = new();

    /// <summary>
    /// Opens a disposal scope on the current async flow. Dispose the returned handle when the
    /// invocation completes to release everything registered during it.
    /// </summary>
    public static IDisposable Begin()
    {
        var resources = new List<IDisposable>();
        Current.Value = resources;
        return new Scope(resources);
    }

    /// <summary>
    /// Registers <paramref name="disposable"/> with the active scope, if any. Returns
    /// <see langword="false"/> when no scope is active (e.g. the converter is used standalone),
    /// so the caller can apply its own fallback cleanup.
    /// </summary>
    public static bool TryRegister(IDisposable disposable)
    {
        var resources = Current.Value;
        if (resources is null)
        {
            return false;
        }
        resources.Add(disposable);
        return true;
    }

    private sealed class Scope(List<IDisposable> resources) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Current.Value = null;
            foreach (var resource in resources)
            {
                try
                {
                    resource.Dispose();
                }
                catch (Exception e)
                {
                    // Best-effort cleanup: one resource's disposal fault must not strand the rest
                    // or escape into the invocation's result path.
                    Debug.WriteLine($"Portico: invocation-scope resource disposal failed: {e}");
                }
            }
            resources.Clear();
        }
    }
}

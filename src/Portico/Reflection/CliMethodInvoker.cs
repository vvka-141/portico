using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Portico.Reflection;

/// <summary>
/// Binds a <see cref="CliInvocation"/> to a route's parameters and dispatches the underlying
/// method — the invoker half of the former <see cref="CliMethodInfo"/> god-class (SOL-78). The
/// actual reflection call is supplied as a delegate so this type stays decoupled from the
/// <see cref="MethodInfo"/> decorator that owns it.
/// </summary>
internal static class CliMethodInvoker
{
    public static async Task<int> InvokeAsync(
        CliRouteModel model,
        CliContext context,
        Func<object?, object?[], object?> invoke,
        object? instance,
        CliInvocation invocation,
        CancellationToken cancellationToken)
    {
        Debug.WriteLine(invocation.ToString("D"));

        // Owns disposables created during argument materialization (e.g. the timeout CTS from
        // CliCancellationTokenTypeConverter). Disposed when this invocation completes, so a
        // long timeout never outlives an early-returning command. See CliInvocationDisposalScope.
        using var disposalScope = CliInvocationDisposalScope.Begin();

        var globalOptionsBundles = context.ToGlobalOptionBundles(invocation);

        // A route HAS matched, so the option metadata exists and we know which values are secret.
        // Must run before OnExecutingAction: middleware (e.g. CliTimingMiddleware) renders the
        // invocation, and by then the redaction has to already be in place.
        invocation.RedactSensitiveOptions(
            model.Options.Concat(globalOptionsBundles.SelectMany(bundle => bundle.GetOptions())));

        // Middleware NESTS, it does not queue: the setup half runs in registration order and the
        // teardown half unwinds in reverse, so a resource A opened is released only after B — which
        // was set up inside it — has released its own. This is the CLI's IActionFilter (CHARTER §3),
        // and ASP.NET Core's filter pipeline runs its "after" code in reverse for the same reason.
        // Materialized once here, not re-enumerated per dispatch. POR-72.
        // Enumerable.Reverse spelled out, not `globalOptionsBundles.Reverse()`. The operand is a
        // CliMiddleware[], and on net8.0 an array binds to MemoryExtensions.Reverse(Span<T>) — which
        // reverses IN PLACE and returns void. Same source, different meaning per target framework;
        // the chained ToArray() is the only reason the compiler catches it rather than the pipeline
        // silently tearing down in registration order on one TFM and reverse on the other.
        var teardownOrder = Enumerable.Reverse(globalOptionsBundles).ToArray();

        // Arm the cleanup BEFORE invoking OnExecutingAction. If a bundle's OnExecutingAction throws
        // partway through (e.g. after CliTraceListener has been attached to Trace.Listeners), the
        // finally block must still run OnActionExecuted so the listener is detached.
        Action onExecuted = () => teardownOrder.ForEach(bundle => bundle.OnActionExecuted(invocation));
        try
        {
            globalOptionsBundles.ForEach(bundle => bundle.OnExecutingAction(invocation));

            var args = BuildArguments(model, invocation);

            // Inject the ambient cancellation token into any CancellationToken parameter that is
            // not explicitly option-bound. BuildArguments leaves default(CancellationToken) in
            // those slots; here we swap in the real token.
            for (int i = 0; i < model.Parameters.Length; i++)
            {
                if (model.Parameters[i] is CliCancellationTokenParameterInfo)
                {
                    args[i] = cancellationToken;
                }
            }

            object? result = invoke(instance, args);

            switch (result)
            {
                case Task<int> intTask:
                    return await intTask.ConfigureAwait(false);
                case int exitCode:
                    return exitCode;
                default:
                    throw new InvalidOperationException(
                        $"Action '{model.Name}' returned a value that is neither int nor Task<int>. " +
                        "This should have been rejected when the application was created.");
            }
        }
        catch (CliOptionMaterializationException e)
        {
            NotifyError(teardownOrder, invocation, e);
            throw new CliExitException(e.Message) { ExitCode = CliExitException.UsageErrorExitCode };
        }
        catch (TargetInvocationException e)
        {
            var inner = e.InnerException ?? new CliExitException(
                $"Action '{model.Name}' failed with an unreported error.")
            { ExitCode = CliExitException.RuntimeErrorExitCode };
            NotifyError(teardownOrder, invocation, inner);
            throw inner;
        }
        catch (Exception e)
        {
            NotifyError(teardownOrder, invocation, e);
            throw;
        }
        finally
        {
            onExecuted.Invoke();
        }
    }

    /// <summary>
    /// Fans out an exception to every global-option bundle's <see cref="CliMiddleware.OnError"/>, in
    /// the same reverse-registration order as <see cref="CliMiddleware.OnActionExecuted"/> — both are
    /// the unwinding half of the pipeline, and running them in opposite directions would let an outer
    /// middleware observe the failure before the inner one it wraps. Bundle-side exceptions are
    /// swallowed: the original exception must propagate, and a buggy telemetry hook should not mask
    /// the user-facing failure.
    /// </summary>
    private static void NotifyError(
        IReadOnlyList<CliMiddleware> bundles,
        CliInvocation invocation,
        Exception exception)
    {
        foreach (var bundle in bundles)
        {
            try
            {
                bundle.OnError(invocation, exception);
            }
            catch (Exception hookFailure)
            {
                Debug.WriteLine($"OnError hook on '{bundle.GetType().FullName}' threw: {hookFailure}");
            }
        }
    }

    private static object?[] BuildArguments(CliRouteModel model, CliInvocation invocation)
    {
        var optionInfos = model.Options;

        var unrecognized = invocation.Options
            .Where(o => false == optionInfos.Any(info => info.IsMatch(o.Name)))
            .Select(o => o.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unrecognized.Count > 0)
        {
            var known = optionInfos
                .SelectMany(info => info.Aliases.IsDefaultOrEmpty ? [] : info.Aliases)
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var suggestions = unrecognized
                .SelectMany(typed => CliRouteMatcher.SuggestOptionMatches(typed, known))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var message = $"Unrecognized option(s): {string.Join(", ", unrecognized)}";
            if (suggestions.Length > 0)
            {
                message += $". Did you mean: {string.Join(", ", suggestions)}?";
            }

            throw new CliExitException(message)
            { ExitCode = CliExitException.UsageErrorExitCode };
        }

        var parameters = model.Parameters;
        object?[] args = new object?[parameters.Length];
        for (int i = 0; i < args.Length; ++i)
        {
            if (parameters[i] is not CliParameterInfo parameter) continue;

            // Framework-supplied parameters (the ambient CancellationToken) don't bind from the
            // invocation — leave their placeholder for InvokeAsync to overwrite. Everything else
            // materializes polymorphically (SOL-82: no more out-of-band type switch).
            args[i] = parameter.IsFrameworkSupplied
                ? parameter.FrameworkPlaceholder
                : parameter.Materialize(invocation);
        }

        return args;
    }
}

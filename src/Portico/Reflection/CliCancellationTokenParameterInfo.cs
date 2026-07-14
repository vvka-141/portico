using System;
using System.Reflection;
using System.Threading;

namespace Portico.Reflection;

/// <summary>
/// Marks a <see cref="CancellationToken"/> parameter that should receive the application's
/// ambient cancellation token (from <c>ProcessAsync(..., CancellationToken)</c>), rather than
/// going through option-based materialization.
/// </summary>
/// <remarks>
/// Emitted by <see cref="CliMethodInfo"/> when a parameter is typed <see cref="CancellationToken"/>
/// and has no explicit <see cref="CliOptionAttribute"/>. Users who want the legacy
/// <c>--timeout 30s</c> behaviour (parsed via <see cref="CliCancellationTokenTypeConverter"/>) keep
/// their explicit <c>[CliOption]</c> and get the option path instead.
/// </remarks>
internal sealed class CliCancellationTokenParameterInfo(ParameterInfo parameter) : CliParameterInfo(parameter)
{
    /// <inheritdoc/>
    public override bool IsFrameworkSupplied => true;

    /// <inheritdoc/>
    public override object? FrameworkPlaceholder => default(CancellationToken);

    // Never called: IsFrameworkSupplied gates the binding pass so the invoker uses
    // FrameworkPlaceholder (then injects the real token) instead of materializing. Kept as a
    // defensive guard so a future caller that ignores the flag fails loudly rather than silently.
    public override object? Materialize(CliInvocation invocation) =>
        throw new InvalidOperationException(
            $"Parameter '{Name}' is filled by the framework via ambient cancellation-token " +
            "injection (IsFrameworkSupplied); it is not materialized from the invocation.");
}

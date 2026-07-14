using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Portico;

/// <summary>
/// Guard clauses. Internal by design: this is a convention Portico inherits from its
/// origin, not part of its public surface.
/// </summary>
internal static partial class ThrowIf
{
    /// <summary>
    /// A bare identifier reads naturally as the subject of a sentence ("console is null");
    /// a member access or call does not, so those get the "returned null" phrasing instead.
    /// </summary>
    [GeneratedRegex(@"^@?[a-z\d_]+$")]
    private static partial Regex VariableName();

    private static bool IsVariableName(string input) => VariableName().IsMatch(input);

    /// <summary>
    /// Throws an <see cref="ArgumentNullException"/> if <paramref name="value"/> is <c>null</c>.
    /// </summary>
    /// <example><code>
    /// public CliRoute(string pattern) => _pattern = ThrowIf.ArgumentNull(pattern);
    /// </code></example>
    [DebuggerNonUserCode, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ArgumentNull<T>(
        T? value,
        string? message = null,
        [CallerArgumentExpression(nameof(value))] string paramName = "")
    {
        if (value is null)
        {
            message ??= IsVariableName(paramName)
                ? $"{paramName} is null."
                : $"{paramName} returned null.";
            throw new ArgumentNullException(paramName, message);
        }

        return value;
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException"/> if <paramref name="value"/> is <c>null</c>,
    /// or an <see cref="ArgumentOutOfRangeException"/> if it is empty or whitespace.
    /// </summary>
    /// <example><code>
    /// public CliOption(string name) => _name = ThrowIf.ArgumentNullOrWhiteSpace(name);
    /// </code></example>
    [DebuggerNonUserCode, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ArgumentNullOrWhiteSpace(
        string? value,
        string? message = null,
        [CallerArgumentExpression(nameof(value))] string paramName = "")
    {
        if (value is null)
        {
            message ??= IsVariableName(paramName)
                ? $"{paramName} is null."
                : $"{paramName} returned null.";
            throw new ArgumentNullException(paramName, message);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            message ??= IsVariableName(paramName)
                ? $"{paramName} is an empty or a whitespace string value."
                : $"{paramName} returned an empty or a whitespace string value.";
            throw new ArgumentOutOfRangeException(paramName, message);
        }

        return value;
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if <paramref name="condition"/> is
    /// <c>false</c> — i.e. it asserts the condition holds.
    /// </summary>
    /// <example><code>
    /// ThrowIf.False(routes.Any(), "The CLI declares no routes.");
    /// </code></example>
    [DebuggerNonUserCode, MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void False(
        bool condition,
        string? message = null,
        [CallerArgumentExpression(nameof(condition))] string argExpression = "")
    {
        if (condition == false)
        {
            message ??= IsVariableName(argExpression)
                ? $"{argExpression} is false."
                : $"{argExpression} evaluated to false.";
            throw new InvalidOperationException(message);
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Portico;

/// <summary>
/// The small helpers the CLI framework needs from its origin library, internalized so
/// Portico can carry zero dependencies. None of this is public surface — a user of a CLI
/// framework should never see it.
/// </summary>
internal static partial class Extensions
{
    /// <summary>
    /// Fluent equivalent of <c>string.Join(separator, self)</c>, so a projection can end a
    /// method chain instead of being wrapped in one.
    /// </summary>
    /// <example><code>
    /// var csv = options.Select(o => o.Name).Join(", ");
    /// </code></example>
    [DebuggerNonUserCode]
    public static string Join(this IEnumerable<string> self, string separator = ",")
    {
        ThrowIf.ArgumentNull(self);
        return string.Join(separator ?? ",", self);
    }

    /// <summary>
    /// Eagerly applies <paramref name="action"/> to every element.
    /// </summary>
    /// <example><code>
    /// flags.Distinct().ForEach(f => builder.Append($" {f}"));
    /// </code></example>
    [DebuggerStepThrough]
    public static void ForEach<T>(this IEnumerable<T> self, Action<T> action)
    {
        ThrowIf.ArgumentNull(self);
        ThrowIf.ArgumentNull(action);

        foreach (var item in self)
        {
            action.Invoke(item);
        }
    }

    /// <summary>
    /// Dequeues elements while they satisfy <paramref name="condition"/>, stopping at the first
    /// that does not. The non-matching element stays on the queue.
    /// </summary>
    /// <example><code>
    /// var segments = queue.DequeueWhile(arg => !IsOptionToken(arg)).ToList();
    /// </code></example>
    public static IEnumerable<T> DequeueWhile<T>(this Queue<T> self, Func<T, bool> condition)
    {
        ThrowIf.ArgumentNull(self);
        ThrowIf.ArgumentNull(condition);

        while (self.TryPeek(out var head) && condition.Invoke(head))
        {
            yield return self.Dequeue();
        }
    }

    /// <summary>
    /// Returns <paramref name="defaultValue"/> when <paramref name="self"/> is null, empty, or
    /// whitespace.
    /// </summary>
    /// <example><code>
    /// var description = attribute.Description.DefaultIfNullOrWhiteSpace(parameter.Name);
    /// </code></example>
    [return: NotNull]
    public static string DefaultIfNullOrWhiteSpace(this string? self, string defaultValue) =>
        string.IsNullOrWhiteSpace(self) ? defaultValue : self;

    /// <summary>
    /// Wraps <paramref name="self"/> in double quotes. Idempotent: already-quoted input is
    /// returned unchanged.
    /// </summary>
    /// <example><code>
    /// var display = value.HasWhiteSpaces() ? value.Quote() : value;
    /// </code></example>
    public static string Quote(this string self)
    {
        ThrowIf.ArgumentNull(self);
        return self.StartsWith('"') && self.EndsWith('"')
            ? self
            : $"\"{self}\"";
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="self"/> contains at least one whitespace
    /// character — i.e. when it would need quoting to survive a round trip through a shell.
    /// </summary>
    /// <example><code>
    /// var display = value.HasWhiteSpaces() ? value.Quote() : value;
    /// </code></example>
    public static bool HasWhiteSpaces(this string self)
    {
        ThrowIf.ArgumentNull(self);
        return WhiteSpace().IsMatch(self);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    /// <summary>
    /// Determines whether a <see cref="TypeConverter"/> can convert a CLI operand — that is,
    /// whether it can accept the string a user actually typed.
    /// </summary>
    public static bool SupportsCliOperandConversion(this TypeConverter converter) =>
        converter is StringConverter ||
        converter.CanConvertFrom(typeof(string));
}

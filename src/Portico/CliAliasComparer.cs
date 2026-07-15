using System;
using System.Collections.Generic;

namespace Portico;

/// <summary>
/// The single source of truth for comparing option aliases.
/// <para>
/// <b>Single-char short aliases (<c>-x</c>) are case-SENSITIVE; every longer form is
/// case-INSENSITIVE.</b> POSIX tools rely on <c>-v</c> and <c>-V</c> being different options —
/// <c>curl -v</c> (verbose) vs <c>curl -V</c> (version), and likewise <c>ssh</c> — so collapsing
/// them would make a near-universal CLI idiom inexpressible. Long forms are conventionally
/// lowercase, so matching <c>--VERBOSE</c> to <c>--verbose</c> costs nothing and is forgiving.
/// </para>
/// <para>
/// This rule was already implemented and documented for the framework's own help/version trigger
/// matcher, but three places encoded it independently and two disagreed: the duplicate-alias guard
/// used <see cref="StringComparer.Ordinal"/> (so <c>-v</c> and <c>-V</c> looked distinct and were
/// allowed) while the option matcher used <see cref="StringComparer.OrdinalIgnoreCase"/> (so each
/// token matched BOTH options). The result was a silent double-binding with exit code 0. Deriving
/// every comparison from this one type is what stops that drift recurring.
/// </para>
/// </summary>
internal sealed class CliAliasComparer : IEqualityComparer<string>
{
    public static readonly CliAliasComparer Instance = new();

    private CliAliasComparer() { }

    /// <summary>A single-char short alias: one leading dash and exactly one character (e.g. <c>-v</c>).</summary>
    private static bool IsSingleCharShort(string alias) => alias.Length == 2 && alias[0] == '-';

    public bool Equals(string? x, string? y)
    {
        if (x is null || y is null) return ReferenceEquals(x, y);

        // If either side is a single-char short alias, compare ordinally. A short and a long alias
        // can never be ordinally equal anyway, so this collapses to "short vs short => Ordinal".
        return (IsSingleCharShort(x) || IsSingleCharShort(y))
            ? string.Equals(x, y, StringComparison.Ordinal)
            : string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(string alias) =>
        IsSingleCharShort(alias)
            ? alias.GetHashCode(StringComparison.Ordinal)
            : alias.GetHashCode(StringComparison.OrdinalIgnoreCase);
}

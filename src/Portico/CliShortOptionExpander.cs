using System.Collections.Generic;

namespace Portico;

/// <summary>
/// POSIX short-option preprocessing. Transforms combined short-form tokens into their canonical
/// equivalents before <see cref="CliInvocation.FromArgs(string[])"/> tokenizes:
/// <list type="bullet">
///   <item><description><c>-abc</c> → <c>-a -b -c</c> when all three are known flag-arity shorts.</description></item>
///   <item><description><c>-abc</c> → <c>-a -b -c bc-remainder</c> when <c>-a</c>, <c>-b</c> are flags and <c>-c</c> is scalar.</description></item>
///   <item><description><c>-n5</c> → <c>-n 5</c> when <c>-n</c> is scalar.</description></item>
/// </list>
/// Preserves tokens untouched in these cases (never introduces ambiguity):
/// <list type="bullet">
///   <item><description>Any token that <see cref="CliInvocation.IsOptionToken"/> says is not an
///     option (negative numbers, bare <c>-</c>, positional arguments).</description></item>
///   <item><description>Long-form (<c>--foo</c>) tokens.</description></item>
///   <item><description>Tokens matching a registered option name exactly (e.g. a user who
///     legitimately declared a multi-char short <c>-foo</c> and types <c>-foo</c>).</description></item>
///   <item><description>Tokens where any character is not a known single-char short — we refuse
///     to guess and let the downstream "unrecognized option" error fire.</description></item>
/// </list>
/// </summary>
internal static class CliShortOptionExpander
{
    public static string[] Expand(
        string[] args,
        CliShortOptionSchema schema,
        IReadOnlySet<string> registeredNames)
    {
        if (schema.IsEmpty || args.Length == 0) return args;

        var result = new List<string>(args.Length);
        foreach (var arg in args)
        {
            var expanded = TryExpandToken(arg, schema, registeredNames);
            if (expanded is null)
            {
                result.Add(arg);
            }
            else
            {
                result.AddRange(expanded);
            }
        }
        return result.ToArray();
    }

    private static string[]? TryExpandToken(
        string token,
        CliShortOptionSchema schema,
        IReadOnlySet<string> registeredNames)
    {
        // Not a short-form option candidate → pass through.
        if (!CliInvocation.IsOptionToken(token)) return null;
        if (token.Length < 3) return null;              // -a: already canonical
        if (token[1] == '-') return null;               // --foo: long form
        if (registeredNames.Contains(token)) return null;    // user legitimately defined this name

        // Token looks like -<char1><char2>... Try to expand char-by-char.
        // First char must be a known single-char short; others depend on the first's arity.
        var stem = token.Substring(1);

        // If the first character isn't a known short, bail — we refuse to guess.
        if (!schema.TryGetArity(stem[0], out var firstArity)) return null;

        // Scalar first char → entire rest is its value: -n5 → -n 5.
        if (firstArity == CliShortOptionArity.Scalar)
        {
            return [$"-{stem[0]}", stem.Substring(1)];
        }

        // Flag first char → iterate. Each subsequent flag extends the expansion; a scalar
        // consumes the rest as its value; unknown char aborts (leave token alone).
        var expanded = new List<string> { $"-{stem[0]}" };
        for (int i = 1; i < stem.Length; i++)
        {
            var c = stem[i];
            if (!schema.TryGetArity(c, out var arity)) return null;

            if (arity == CliShortOptionArity.Scalar)
            {
                expanded.Add($"-{c}");
                if (i + 1 < stem.Length)
                {
                    expanded.Add(stem.Substring(i + 1));
                }
                return expanded.ToArray();
            }

            expanded.Add($"-{c}");
        }
        return expanded.ToArray();
    }
}

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
///   <item><description>Tokens whose short is a map option (<c>-e[region] eu</c>) — the
///     <c>[key]</c> suffix must reach the tokenizer intact, so the token is never split.</description></item>
/// </list>
/// <para>
/// <b>Bundling depends on an application-wide agreement about each letter's arity</b>, because
/// expansion runs on raw argv before any route has matched — <c>-fx</c> must be split before the
/// parser can know which command it belongs to, so there is no per-route schema to consult.
/// </para>
/// <para>
/// When two commands declare the same short letter with <em>different</em> arities — say <c>-f</c> as
/// a <c>CliFlag?</c> on one route and as a <c>string</c> on another — the letter is removed from the
/// schema, and <b>bundling stops working for it everywhere</b>, including on a command that declares
/// it consistently. The options themselves are unaffected: <c>-f -x</c> still binds on every route,
/// only the glued <c>-fx</c> form stops expanding, and the resulting error names the split
/// (<c>"Did you mean: -f, -x?"</c>). The conflict is reported as a trace warning at
/// <see cref="CliApplication.Create"/> naming both routes, and the letters involved are on
/// <see cref="CliShortOptionSchema.ConflictingShortNames"/> (POR-119).
/// </para>
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

        // -f=bar / -f:bar — an explicit assignment, not a glued cluster. Pass it through so the
        // invocation's assignment split reads the value as `bar`, which is what the framework has
        // meant by this form since the port (CliInvocation_FromArgs_Should.Split_Option_Assignment_
        // Syntax). Expanding it here instead would glue "=bar" on as the value, and the two entry
        // paths — a real argv and a command-line string — would disagree about the same token
        // (POR-56).
        if (token[2] is '=' or ':') return null;

        // Token looks like -<char1><char2>... Try to expand char-by-char.
        // First char must be a known single-char short; others depend on the first's arity.
        var stem = token.Substring(1);

        // If the first character isn't a known short, bail — we refuse to guess.
        if (!schema.TryGetArity(stem[0], out var firstArity)) return null;

        // Map first char (-e[region] eu) → leave the whole token for the tokenizer, which
        // understands the bracket-key syntax. Splitting here would tear the [key] off the option
        // and it would never bind (POR-58).
        if (firstArity == CliShortOptionArity.Map)
        {
            return null;
        }

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

            // A map short mid-cluster carries a [key] we cannot represent in a split — leave the
            // whole token intact for the tokenizer (POR-58).
            if (arity == CliShortOptionArity.Map) return null;

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

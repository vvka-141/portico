using System.Collections.Generic;

namespace Portico;

/// <summary>
/// Describes the arity of a single-character short option — flag (takes no value), scalar
/// (takes exactly one value), or map (the bracket-key <c>-e[region] eu</c> form). The POSIX
/// preprocessing step (<see cref="CliShortOptionExpander.Expand"/>) uses this to decide how to
/// split combined tokens: <c>-abc</c> becomes <c>-a -b -c</c> if all three are flags; <c>-n5</c>
/// becomes <c>-n 5</c> if <c>-n</c> is scalar; mixed cases split at the first non-flag. A
/// <see cref="Map"/> short is never split — its <c>[key]</c> suffix must reach the tokenizer intact.
/// </summary>
internal enum CliShortOptionArity
{
    Flag,
    Scalar,
    Map,
}

/// <summary>
/// Maps single-character short option names (without the leading dash) to their arity. Built
/// once at <see cref="CliApplication.Create"/> time from the union of all registered methods'
/// options. Multi-character short names (e.g. <c>-foo</c>) are excluded here — they are not
/// split by the POSIX preprocessor and work as registered.
/// </summary>
internal sealed class CliShortOptionSchema
{
    public static readonly CliShortOptionSchema Empty = new(new Dictionary<char, CliShortOptionArity>());

    private readonly IReadOnlyDictionary<char, CliShortOptionArity> _arity;

    public CliShortOptionSchema(IReadOnlyDictionary<char, CliShortOptionArity> arity)
    {
        _arity = arity;
    }

    public bool IsEmpty => _arity.Count == 0;

    public bool TryGetArity(char shortName, out CliShortOptionArity arity) =>
        _arity.TryGetValue(shortName, out arity);
}

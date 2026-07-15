using System.Collections.Generic;
using System.Collections.Immutable;

namespace Portico;

/// <summary>
/// Parsed collection option — multiple values follow the option
/// (e.g. <c>--files a.txt b.txt c.txt</c>).
/// </summary>
public sealed record CliCollectionOptionCapture(string Name, ImmutableArray<string> Values)
    : CliOptionCapture(Name), ICliCollectionCapture
{
    // Explicit: the record's own Values stays ImmutableArray<string> (the materializer
    // pattern-matches on it), while the interface exposes the same values as IEnumerable<string>
    // so ToString and the option materializer can treat scalars and collections uniformly.
    IEnumerable<string> ICliCollectionCapture.Values => Values;
}

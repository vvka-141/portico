using System.Collections.Immutable;

namespace Portico;

/// <summary>
/// Parsed collection option — multiple values follow the option
/// (e.g. <c>--files a.txt b.txt c.txt</c>).
/// </summary>
public sealed record CliCollectionOptionCapture(string Name, ImmutableArray<string> Values)
    : CliOptionCapture(Name);

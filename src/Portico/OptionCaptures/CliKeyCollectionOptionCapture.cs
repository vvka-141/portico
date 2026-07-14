using System.Collections.Immutable;

namespace Portico;

/// <summary>
/// Parsed keyed collection option — a keyed token with multiple values
/// (e.g. <c>--envs[prod] us-east us-west eu-west</c>).
/// </summary>
public sealed record CliKeyCollectionOptionCapture(string Name, string Key, ImmutableArray<string> Values)
    : CliOptionCapture(Name), ICliMapOptionCapture;

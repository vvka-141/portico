using System.Collections.Generic;

namespace Portico;

/// <summary>Parsed scalar option — a single value follows the option (e.g. <c>--output file.txt</c>).</summary>
public sealed record CliScalarOptionCapture(string Name, string Value)
    : CliOptionCapture(Name), ICliCollectionCapture
{
    /// <summary>Exposes the single <see cref="Value"/> as a one-element sequence.</summary>
    public IEnumerable<string> Values => [Value];
}

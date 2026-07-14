using System.Collections.Generic;

namespace Portico;

/// <summary>
/// An option capture that carries one-or-more string values — implemented by both scalars
/// (single value, see <see cref="CliScalarOptionCapture"/>) and true collections (see
/// <see cref="CliCollectionOptionCapture"/>). Lets the option materializer treat both uniformly.
/// </summary>
public interface ICliCollectionCapture : ICliOptionCapture
{
    /// <summary>The captured values, in source order.</summary>
    IEnumerable<string> Values { get; }
}

using System;

namespace Portico.Reflection;

/// <summary>
/// Thrown when a CLI option cannot be materialized from the parsed command line — either
/// because the value doesn't convert to the declared type, or violates DataAnnotations, or
/// (for required options) isn't supplied at all. The framework catches these at the
/// usage-error boundary and formats them to stderr with exit code <c>2</c>.
/// </summary>
internal sealed class CliOptionMaterializationException : FormatException
{
    public CliOptionMaterializationException(string message) : base(message) { }

    /// <summary>
    /// Preserves the underlying conversion failure as <see cref="Exception.InnerException"/>
    /// so tests and telemetry can inspect the original <see cref="FormatException"/> /
    /// <see cref="OverflowException"/> / etc. without message-substring parsing.
    /// </summary>
    public CliOptionMaterializationException(string message, Exception innerException)
        : base(message, innerException) { }
}

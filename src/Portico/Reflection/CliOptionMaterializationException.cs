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
    // Every message on this path embeds a value the user typed ("Value 'abc' for option '--amount' is
    // invalid."). That is attacker-influenced input on its way to stderr, so it is sanitized here —
    // one choke point rather than a dozen interpolation sites, each of which someone would eventually
    // forget (POR-48; the same lesson as the Sensitive leaks in POR-24/POR-17).
    public CliOptionMaterializationException(string message) : base(CliSanitizer.Sanitize(message)) { }

    /// <summary>
    /// Preserves the underlying conversion failure as <see cref="Exception.InnerException"/>
    /// so tests and telemetry can inspect the original <see cref="FormatException"/> /
    /// <see cref="OverflowException"/> / etc. without message-substring parsing.
    /// </summary>
    public CliOptionMaterializationException(string message, Exception innerException)
        : base(CliSanitizer.Sanitize(message), innerException) { }
}

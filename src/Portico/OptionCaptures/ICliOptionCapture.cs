namespace Portico;

/// <summary>
/// Shared interface for every kind of parsed option on a <see cref="CliInvocation"/>. The
/// framework's route matcher and option materializer work against this contract so new capture
/// shapes can be added without touching downstream code.
/// </summary>
public interface ICliOptionCapture
{
    /// <summary>The option's leading token (e.g. <c>--verbose</c>, <c>-v</c>, <c>--config</c>).</summary>
    string Name { get; }
}

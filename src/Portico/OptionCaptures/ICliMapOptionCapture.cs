namespace Portico;

/// <summary>
/// A map-shaped option capture — the <c>--config[env] prod</c> syntax. Implementations expose
/// a <see cref="Key"/> in addition to <see cref="ICliOptionCapture.Name"/>; the framework
/// materializes these into <see cref="System.Collections.Generic.IDictionary{TKey,TValue}"/>
/// parameters.
/// </summary>
public interface ICliMapOptionCapture : ICliOptionCapture
{
    /// <summary>The key extracted from the bracket suffix (e.g. <c>env</c> in <c>--config[env]</c>).</summary>
    string Key { get; }
}

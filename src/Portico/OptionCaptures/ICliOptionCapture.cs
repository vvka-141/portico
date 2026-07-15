namespace Portico;

/// <summary>
/// The root contract of the option-capture family: <see cref="Name"/>, common to every parsed
/// option shape. Implemented by the base record <see cref="CliOptionCapture"/> (so all six concrete
/// captures carry it) and extended by the shape-specific <see cref="ICliCollectionCapture"/> and
/// <see cref="ICliMapOptionCapture"/>, which downstream code discriminates on. Centralising
/// <see cref="Name"/> here is what lets those sub-interfaces expose it without redeclaring it.
/// </summary>
public interface ICliOptionCapture
{
    /// <summary>The option's leading token (e.g. <c>--verbose</c>, <c>-v</c>, <c>--config</c>).</summary>
    string Name { get; }
}

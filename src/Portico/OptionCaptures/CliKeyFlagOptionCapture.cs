namespace Portico;

/// <summary>
/// Parsed keyed flag option — a keyed token with no value (e.g. <c>--feature[dark-mode]</c>).
/// </summary>
public sealed record CliKeyFlagOptionCapture(string Name, string Key)
    : CliOptionCapture(Name), ICliMapOptionCapture;

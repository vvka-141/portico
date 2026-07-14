namespace Portico;

/// <summary>
/// Parsed keyed scalar option — a keyed token with one value
/// (e.g. <c>--config[env] production</c>).
/// </summary>
public sealed record CliKeyValueOptionCapture(string Name, string Key, string Value)
    : CliOptionCapture(Name), ICliMapOptionCapture;

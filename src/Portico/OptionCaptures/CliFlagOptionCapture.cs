namespace Portico;

/// <summary>Parsed flag option — a bare option token with no following value (e.g. <c>--verbose</c>).</summary>
public sealed record CliFlagOptionCapture(string Name) : CliOptionCapture(Name);

using System;

namespace Portico.Reflection;

/// <summary>
/// A single element of a compiled route (e.g. <c>init {path} with-default</c> compiles to
/// three segments: literal, argument, literal). Discriminated union — every instance is one of:
/// <list type="bullet">
///   <item><see cref="CliLiteralSegment"/> — a fixed string that must match exactly</item>
///   <item><see cref="CliArgumentSegment"/> — a placeholder bound to a method parameter</item>
/// </list>
/// <see cref="CliPlaceholderSegment"/> is an internal transient used during route parsing; it
/// is replaced by <see cref="CliArgumentSegment"/> before a method's segments are finalized.
/// </summary>
internal abstract record CliRouteSegment;

/// <summary>
/// A literal segment — a fixed route token (e.g. <c>init</c>) matched case-insensitively against the
/// user's input. Route literals are exact tokens, not patterns (alias/alternation syntax is not a
/// feature — see <c>RejectMultipleRouteAttributes</c>), so matching is an ordinal, culture-stable
/// <see cref="string.Equals(string, string, StringComparison)"/> — no regex to build per dispatch and
/// no metacharacter hazard.
/// </summary>
internal sealed record CliLiteralSegment(string Text) : CliRouteSegment
{
    public bool Matches(string token) => string.Equals(Text, token, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A positional-argument segment — the user-supplied value binds to <see cref="Argument"/>'s parameter.</summary>
internal sealed record CliArgumentSegment(CliArgumentAttribute Argument) : CliRouteSegment;

/// <summary>
/// Transient sentinel emitted by the route parser for each <c>{name}</c> token. Replaced by
/// <see cref="CliArgumentSegment"/> once the method's parameters are known (in
/// <see cref="CliMethodInfo"/>'s ctor). Never survives construction.
/// </summary>
internal sealed record CliPlaceholderSegment(string Name) : CliRouteSegment;

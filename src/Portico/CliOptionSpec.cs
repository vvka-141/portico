using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Portico;

/// <summary>
/// Parses a <c>[CliOption]</c> specification string (e.g. <c>--name|-n</c>) into its aliases plus
/// the compiled runtime matcher. This is the pure-mechanism half of <see cref="CliOptionAttribute"/>,
/// extracted (SOL-79 / ROADMAP C2) so spec parsing and matching are unit-testable without
/// constructing an attribute. The attribute keeps only its declarative surface and the documented
/// virtual binder seams (<c>CanAccept</c> / <c>GetValueComparer</c> / <c>AllowsCsv</c>).
/// </summary>
internal sealed class CliOptionSpec
{
    // One or more dash-prefixed aliases separated by '|', e.g. --name|-n.
    private static readonly Regex SpecificationRegex = new(
        @"^$option(\|$option)*$".Replace("$option", "(?<option>--?[^|]+)"));

    // The alias name with its leading dashes stripped (lookbehind), allowing internal single dashes.
    private static readonly Regex OptionRegex = new(@"(?<=^[-]{1,2})[\w\?]+(?:[-]+[\w\?]+)*$");

    // Option aliases are exact dashed tokens (e.g. --name, -n), not patterns, so a matched name is
    // just set membership — free of the metacharacter hazard a `^(?:{aliases})$` regex carried (an
    // alias like `-?` would otherwise mean "optional dash"). Case handling is delegated to
    // CliAliasComparer: -v and -V are DIFFERENT options; --verbose and --VERBOSE are the same one.
    private readonly HashSet<string> _aliasSet;

    private CliOptionSpec(
        IReadOnlyList<string> aliases,
        IReadOnlyList<string> longOptionNames,
        IReadOnlyList<string> shortOptionNames,
        string pipeSeparatedAliases)
    {
        Aliases = aliases;
        LongOptionNames = longOptionNames;
        ShortOptionNames = shortOptionNames;
        PipeSeparatedAliases = pipeSeparatedAliases;
        _aliasSet = new HashSet<string>(aliases, CliAliasComparer.Instance);
    }

    /// <summary>Every alias, with its leading dashes, in declaration order (e.g. <c>--name</c>, <c>-n</c>).</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>Long alias names (declared with <c>--</c>), dash-stripped.</summary>
    public IReadOnlyList<string> LongOptionNames { get; }

    /// <summary>Short alias names (declared with a single <c>-</c>), dash-stripped.</summary>
    public IReadOnlyList<string> ShortOptionNames { get; }

    /// <summary>Canonical round-trip form: long aliases first, longest within a group first.</summary>
    public string PipeSeparatedAliases { get; }

    /// <summary>True when <paramref name="optionName"/> (with leading dashes) matches an alias.</summary>
    public bool IsMatch(string optionName) => _aliasSet.Contains(optionName);

    /// <summary>
    /// Parses a specification string into a <see cref="CliOptionSpec"/>. Throws
    /// <see cref="ArgumentException"/> for a malformed pattern, a duplicate alias, or an empty spec.
    /// </summary>
    public static CliOptionSpec Parse(string specification)
    {
        var spec = ThrowIf.ArgumentNullOrWhiteSpace(specification);
        var match = SpecificationRegex.Match(spec);
        if (!match.Success)
        {
            throw new ArgumentException("Invalid pattern format. Expected: --option1|--option2|-o|...",
                nameof(specification));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var longNames = new List<string>();
        var shortNames = new List<string>();

        foreach (Capture capture in match.Groups["option"].Captures)
        {
            var nameMatch = OptionRegex.Match(capture.Value);
            var value = nameMatch.Value;
            if (!nameMatch.Success)
            {
                throw new ArgumentException($"Invalid parameter name option: {capture}", nameof(specification));
            }

            if (!names.Add(capture.Value))
            {
                throw new ArgumentException($"Duplicate option name detected: '{value}'", nameof(specification));
            }

            if (capture.Value.StartsWith("--")) longNames.Add(value);
            else shortNames.Add(value);
        }

        if (longNames.Count == 0 && shortNames.Count == 0)
        {
            throw new ArgumentException("At least one long or short name must be specified.", nameof(specification));
        }

        var pipeSeparated = longNames
            .OrderByDescending(n => n.Length)
            .Select(n => $"--{n}")
            .Union(shortNames
                .OrderByDescending(n => n.Length)
                .Select(n => $"-{n}"))
            .Join("|");

        return new CliOptionSpec(
            names.ToArray().AsReadOnly(),
            longNames.AsReadOnly(),
            shortNames.AsReadOnly(),
            pipeSeparated);
    }
}

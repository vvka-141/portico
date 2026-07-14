using System;
using System.Collections.Generic;
using System.Linq;

namespace Portico.Reflection;

/// <summary>
/// Routing and ranking over a <see cref="CliRouteModel"/> — the matcher half of the former
/// <see cref="CliMethodInfo"/> god-class (SOL-78). Pure functions: no state, no side effects.
/// </summary>
internal static class CliRouteMatcher
{
    /// <summary>
    /// True when the invocation's positional segments match this route's segment shape: same
    /// count, and every literal segment matches the corresponding token. Argument segments accept
    /// any token here — value binding happens later, during materialization.
    /// </summary>
    public static bool IsMatch(CliRouteModel model, CliInvocation invocation)
    {
        var segments = model.Segments;
        var supplied = invocation.Segments.Length;

        // A trailing run of optional arguments may be omitted from the right, so the acceptable
        // token count is a range, not an equality. Dropped slots are only ever at the tail, so the
        // indices below still line up.
        if (supplied < model.MinSegmentCount || supplied > segments.Length)
        {
            return false;
        }

        for (int i = 0; i < supplied; ++i)
        {
            if (segments[i] is CliLiteralSegment literal &&
                !literal.Matches(invocation.Segments[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Scores how well the invocation's options fit this route: +1 per matched option, −1 per
    /// missing required option, −1 per unrecognized option. Used to break ties between routes
    /// whose segment shapes matched equally.
    /// </summary>
    public static double RankByOptions(CliRouteModel model, CliInvocation invocation)
    {
        var optionInfos = model.Options;

        var matchedOptions = optionInfos
            .Count(optionInfo => optionInfo.IsIn(invocation));

        var missingOptions = optionInfos
            .Where(info => info.IsOptional == false)
            .Count(optionInfo => optionInfo.IsNotIn(invocation));

        var unrecognizedOptions = invocation.Options
            .Count(optionCapture => false == optionInfos.Any(info => info.IsMatch(optionCapture.Name)));

        return matchedOptions - missingOptions - unrecognizedOptions;
    }

    /// <summary>
    /// Ranks known option aliases by Levenshtein distance from <paramref name="typed"/> and
    /// returns up to three candidates within a length-scaled tolerance. Dash-prefix-aware: short
    /// typos like <c>-vebose</c> still match <c>--verbose</c> because the comparison is done on
    /// dash-stripped stems, but the suggestion string preserves the canonical leading dashes.
    /// </summary>
    public static IEnumerable<string> SuggestOptionMatches(string typed, IReadOnlyList<string> known)
    {
        if (known.Count == 0 || string.IsNullOrWhiteSpace(typed))
        {
            yield break;
        }

        static string Strip(string s) => s.TrimStart('-');

        var typedStem = Strip(typed);
        if (typedStem.Length == 0)
        {
            yield break;
        }

        var tolerance = Math.Max(2, typedStem.Length / 3);

        var ranked = known
            .Select(alias => (Alias: alias, Distance: CliFuzzyMatch.LevenshteinDistance(typedStem, Strip(alias))))
            .Where(p => p.Distance <= tolerance)
            .OrderBy(p => p.Distance)
            .ThenBy(p => p.Alias, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(p => p.Alias);

        foreach (var s in ranked)
        {
            yield return s;
        }
    }
}

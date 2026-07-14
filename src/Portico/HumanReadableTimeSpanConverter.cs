using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Portico;

/// <summary>
/// Converts human-readable durations — <c>"30 seconds"</c>, <c>"5 min"</c>, <c>"1.5 hours"</c>,
/// <c>"2 days 4 hrs"</c> — into a <see cref="TimeSpan"/>. Internal: the CLI reaches it through
/// <see cref="MultiFormatTimeSpanConverter"/>, never a user directly.
/// </summary>
internal sealed partial class HumanReadableTimeSpanConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string input
            ? Parse(input)
            : base.ConvertFrom(context, culture, value);

    /// <summary>
    /// Attempts the parse, returning <c>false</c> rather than throwing on a malformed input.
    /// </summary>
    /// <example><code>
    /// if (HumanReadableTimeSpanConverter.TryParse("90 seconds", out var timeout)) { /* ... */ }
    /// </code></example>
    public static bool TryParse(string text, out TimeSpan timeout)
    {
        timeout = TimeSpan.Zero;
        if (!TimeSpanPattern().IsMatch(text))
        {
            return false;
        }

        try
        {
            timeout = Parse(text) ?? throw new FormatException();
            return true;
        }
        catch (FormatException e)
        {
            Debug.WriteLine(e.Message);
            return false;
        }
    }

    private static TimeSpan? Parse(string input)
    {
        ThrowIf.ArgumentNullOrWhiteSpace(
            input,
            "Input string cannot be null, empty, or consist only of white-space characters.");

        // Normalize every accepted spelling of a unit onto its canonical name, so the fraction
        // loop below only has four cases to switch on.
        var normalized = input.Trim('\'', '"').ToLowerInvariant();
        normalized = SecondsAlias().Replace(normalized, "seconds");
        normalized = MinutesAlias().Replace(normalized, "minutes");
        normalized = HoursAlias().Replace(normalized, "hours");
        normalized = DaysAlias().Replace(normalized, "days");

        var timespanMatch = TimeSpanPattern().Match(normalized);
        if (false == timespanMatch.Success)
        {
            throw new FormatException(
                $"The input string '{input}' does not match the expected time span format.");
        }

        var result = TimeSpan.Zero;
        foreach (Capture capture in timespanMatch.Groups["fraction"].Captures)
        {
            var fractionMatch = FractionPattern().Match(capture.Value);
            if (!fractionMatch.Success)
            {
                throw new FormatException(
                    $"The time span fraction '{capture.Value}' is not in a recognized format.");
            }

            var valueText = fractionMatch.Groups["value"].Value;
            var units = fractionMatch.Groups["units"].Value;

            if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"The value '{valueText}' is not a valid number.");
            }

            result += units switch
            {
                "seconds" => TimeSpan.FromSeconds(value),
                "minutes" => TimeSpan.FromMinutes(value),
                "hours" => TimeSpan.FromHours(value),
                "days" => TimeSpan.FromDays(value),
                _ => throw new FormatException(
                    $"Unrecognized time unit: '{units}'. Valid units are 'seconds', 'minutes', 'hours', and 'days'.")
            };
        }

        return result;
    }

    [GeneratedRegex(@"^(?<fraction>[\d\.]+\s+\w+)(?:\s+(?<fraction>[\d\.]+\s+\w+))*$")]
    private static partial Regex TimeSpanPattern();

    [GeneratedRegex(@"(?<value>(?:\.\d+|\d+(?:\.\d*)?))\s+(?<units>\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex FractionPattern();

    [GeneratedRegex(@"\b(?:seconds?|secs?)\b")]
    private static partial Regex SecondsAlias();

    [GeneratedRegex(@"\bminutes?|mins?\b")]
    private static partial Regex MinutesAlias();

    [GeneratedRegex(@"\bhours?|hrs?\b")]
    private static partial Regex HoursAlias();

    [GeneratedRegex(@"\bdays?\b")]
    private static partial Regex DaysAlias();
}

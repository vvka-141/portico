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
        catch (OverflowException e)
        {
            // '999999999d' is not a parse failure, it is a number TimeSpan cannot hold. Returning
            // false lets the caller reach its own message, which names the accepted forms, instead
            // of surfacing the BCL's arithmetic-overflow text at the user.
            Debug.WriteLine(e.Message);
            return false;
        }
    }

    private static TimeSpan? Parse(string input)
    {
        ThrowIf.ArgumentNullOrWhiteSpace(
            input,
            "Input string cannot be null, empty, or consist only of white-space characters.");

        var match = TimeSpanPattern().Match(input.Trim('\'', '"'));
        if (!match.Success)
        {
            throw new FormatException(
                $"The input string '{input}' does not match the expected time span format.");
        }

        var values = match.Groups["value"].Captures;
        var units = match.Groups["unit"].Captures;

        var result = TimeSpan.Zero;
        for (int i = 0; i < values.Count; i++)
        {
            if (!double.TryParse(values[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormatException($"The value '{values[i].Value}' is not a valid number.");
            }

            result += ToTimeSpan(value, units[i].Value);
        }

        return result;
    }

    /// <summary>
    /// The unit table. Compact single letters (<c>90s</c>, <c>1h30m</c>) sit beside the spelled-out
    /// forms because operators arrive from Go durations, <c>kubectl --timeout</c>, systemd and
    /// Prometheus, where the compact spelling is the normal one.
    /// </summary>
    private static TimeSpan ToTimeSpan(double value, string unit) => unit.ToLowerInvariant() switch
    {
        "ms" or "msec" or "msecs" or "millisecond" or "milliseconds" => TimeSpan.FromMilliseconds(value),
        "s" or "sec" or "secs" or "second" or "seconds" => TimeSpan.FromSeconds(value),
        "m" or "min" or "mins" or "minute" or "minutes" => TimeSpan.FromMinutes(value),
        "h" or "hr" or "hrs" or "hour" or "hours" => TimeSpan.FromHours(value),
        "d" or "day" or "days" => TimeSpan.FromDays(value),
        _ => throw new FormatException(
            $"Unrecognized time unit: '{unit}'. Valid units are ms, s, m, h and d, " +
            "or their spelled-out forms (milliseconds, seconds, minutes, hours, days).")
    };

    /// <summary>
    /// One number-and-unit pair, repeated. The whole input must be pairs — a trailing bare number
    /// or a stray word fails the anchors rather than being silently ignored.
    /// </summary>
    /// <remarks>
    /// This replaced a normalize-then-reparse design that rewrote every unit alias onto its
    /// canonical spelling with four <c>Regex.Replace</c> passes before matching. That approach could
    /// not express the compact forms at all — <c>\b</c> never fires between the <c>1</c> and the
    /// <c>h</c> of <c>1h30m</c> — and two of its four alias patterns were ungrouped
    /// (<c>\bminutes?|mins?\b</c> parses as <c>(\bminutes?)|(mins?\b)</c>, so the second alternative
    /// had no leading word boundary and rewrote <c>admin</c> to <c>adminutes</c>). That was latent
    /// only because the mandatory whitespace between number and unit kept such inputs from ever
    /// reaching the aliases. Matching pairs directly and looking the unit up in a table removes the
    /// alias pass, and with it the whole class of defect (POR-147).
    /// <para>
    /// <c>[a-z]+</c> rather than <c>\w+</c> for the unit is load-bearing: <c>\w</c> includes digits,
    /// so a greedy unit would swallow <c>h30m</c> whole out of <c>1h30m</c>.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"^(?:\s*(?<value>\d+(?:\.\d+)?|\.\d+)\s*(?<unit>[a-z]+))+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeSpanPattern();
}

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Portico;

/// <summary>
/// Parses a <see cref="TimeSpan"/> from any of the three shapes a user might reasonably type:
/// the .NET format (<c>"00:00:30"</c>), a human-readable duration (<c>"30 seconds"</c>), or an
/// ISO 8601 duration (<c>"PT30S"</c>). This is what makes a <c>TimeSpan</c> parameter usable
/// from a terminal.
/// </summary>
internal sealed partial class MultiFormatTimeSpanConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    /// <inheritdoc />
    [DebuggerStepThrough]
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        ThrowIf.ArgumentNull(value, "Value cannot be null.");
        if (value is string text)
        {
            return Parse(text);
        }

        throw new ArgumentException(
            "The provided value must be a string representing a TimeSpan.", nameof(value));
    }

    private static object? Parse(string timeoutText)
    {
        ThrowIf.ArgumentNullOrWhiteSpace(timeoutText, "Timeout text cannot be null or empty");

        if (BareNumber().IsMatch(timeoutText))
        {
            throw new FormatException(
                $"Ambiguous duration '{timeoutText.Trim()}' — a bare number means DAYS to .NET, so " +
                $"'{timeoutText.Trim()}' would be {timeoutText.Trim()} days. Say which unit you mean: " +
                $"'{timeoutText.Trim()}s', '{timeoutText.Trim()} seconds', '{timeoutText.Trim()}m', " +
                $"'{timeoutText.Trim()} days' — or use the .NET form '00:00:{timeoutText.Trim()}'.");
        }

        try
        {
            if (TimeSpan.TryParse(timeoutText, out var timeout) ||
                HumanReadableTimeSpanConverter.TryParse(timeoutText, out timeout))
            {
                return timeout;
            }

            return XmlConvert.ToTimeSpan(timeoutText);
        }
        catch (FormatException ex)
        {
            // Every non-ISO-8601 failure lands here, so this is the message a mistyped duration
            // actually gets. It used to be "Invalid timeout format: X" — true, and useless: it
            // restated the input and named none of the four things that would have worked.
            throw new FormatException(AcceptedForms(timeoutText), ex);
        }
    }

    internal static string AcceptedForms(string rejected) =>
        $"Invalid duration '{rejected.Trim()}'. Accepted forms: compact ('90s', '1h30m', '500ms'), " +
        $"spelled out ('30 seconds', '5 min', '2 days 4 hrs'), the .NET form ('00:00:30', " +
        $"'1.12:00:00'), or ISO 8601 ('PT30S'). Units are ms, s, m, h, d.";

    /// <summary>
    /// A number and nothing else. <c>TimeSpan.TryParse</c> reads it as a <b>day</b> count, so
    /// <c>--timeout 30</c> silently binds thirty days — an outage on a <c>drain</c> or a
    /// <c>migrate</c>, and the only value in this converter that fails silently rather than loudly.
    /// </summary>
    /// <remarks>
    /// Refused rather than reinterpreted. Reading it as seconds would be friendlier and is the wrong
    /// call: the same string would then mean one thing in Portico and another in every other .NET
    /// tool, and a <see cref="TimeSpan"/> that quietly disagrees with the BCL is worse than one that
    /// declines to guess (POR-147). The behaviour is a .NET-wide trap — all six surveyed CLI
    /// frameworks inherit it — not a Portico invention; Portico is only the one that had already
    /// promised to understand <c>"5 min"</c>, which is what makes it inconsistent here rather than
    /// merely inherited.
    /// <para>
    /// Anything containing a colon or a unit is unaffected: <c>30.12:00:00</c> and <c>30 days</c>
    /// both fail this pattern and parse as before.
    /// </para>
    /// </remarks>
    [GeneratedRegex(@"^\s*\d+(?:\.\d+)?\s*$")]
    private static partial Regex BareNumber();
}

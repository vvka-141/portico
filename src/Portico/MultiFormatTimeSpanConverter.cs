using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Xml;

namespace Portico;

/// <summary>
/// Parses a <see cref="TimeSpan"/> from any of the three shapes a user might reasonably type:
/// the .NET format (<c>"00:00:30"</c>), a human-readable duration (<c>"30 seconds"</c>), or an
/// ISO 8601 duration (<c>"PT30S"</c>). This is what makes a <c>TimeSpan</c> parameter usable
/// from a terminal.
/// </summary>
internal sealed class MultiFormatTimeSpanConverter : TypeConverter
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
            throw new FormatException($"Invalid timeout format: {timeoutText}", ex);
        }
    }
}

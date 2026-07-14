using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace Portico;

internal sealed partial class CliCancellationTokenTypeConverter : TypeConverter
{
    // Compact duration form common in CLI tools: "30s", "5m", "2h", "1d", "1.5h".
    [GeneratedRegex(@"^\s*(?<value>\d+(?:\.\d+)?)\s*(?<unit>s|m|h|d)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CompactDurationRegex { get; }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    [DebuggerStepThrough]
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        return Parse(value.ToString() ?? throw new InvalidOperationException("Invalid timeout text"));
    }

    private object? Parse(string timeoutText)
    {
        if (string.IsNullOrWhiteSpace(timeoutText))
        {
            throw new ArgumentException("Timeout text cannot be null or empty", nameof(timeoutText));
        }

        try
        {
            if (TryParseCompact(timeoutText, out var timeout) ||
                TimeSpan.TryParse(timeoutText, out timeout) ||
                HumanReadableTimeSpanConverter.TryParse(timeoutText, out timeout))
            {
                return CreateToken(timeout);
            }

            // ISO 8601 duration format (e.g. "PT30S", "PT2M")
            timeout = XmlConvert.ToTimeSpan(timeoutText);
            return CreateToken(timeout);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Invalid timeout format: {timeoutText}", ex);
        }
    }

    // new CancellationTokenSource(TimeSpan) schedules an internal Timer that lives until the
    // timeout elapses. Returning only the Token would orphan the source — a real leak in
    // long-running / hosted processes (notably CliTestHarness, which runs many invocations in
    // one process). Tie the source to the invocation's disposal scope so it (and its timer) is
    // released the moment the command completes, regardless of how early that is. Outside an
    // invocation (standalone converter use) there is no scope to own it; the one-shot timer
    // still fires and is reclaimed at/after the timeout — the original behavior — which is
    // acceptable for that rare path.
    private static CancellationToken CreateToken(TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        CliInvocationDisposalScope.TryRegister(cts);
        return cts.Token;
    }

    private static bool TryParseCompact(string text, out TimeSpan timeout)
    {
        var match = CompactDurationRegex.Match(text);
        if (!match.Success)
        {
            timeout = TimeSpan.Zero;
            return false;
        }

        var value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        timeout = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            "d" => TimeSpan.FromDays(value),
            _ => TimeSpan.Zero,
        };
        return true;
    }
}
using System;
using System.Diagnostics;
using System.IO;

namespace Portico;

internal class CliTraceListener : TraceListener
{
    private readonly TraceLevel _level;
    private readonly TextWriter _writer;

    // Writer is required — the sink is the application's ICliConsole (SOL-80), never a
    // Console.Out default, so trace output honours redirection and testing.
    public CliTraceListener(TraceLevel level, TextWriter writer)
    {
        _level = level;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public override void Write(string? message)
    {
        if (message is null) return;
        // Assuming the least severe level for direct writes: Verbose
        if (ShouldTrace(TraceEventType.Verbose))
        {
            _writer.Write(message);
        }
    }

    public override void WriteLine(string? message)
    {
        if (message is null) return;
        // Assuming the least severe level for direct writes: Verbose
        if (ShouldTrace(TraceEventType.Verbose))
        {
            _writer.WriteLine(message);
        }
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        if (ShouldTrace(eventType))
        {
            _writer.WriteLine($"{eventType}: {message}");
        }
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
    {
        if (ShouldTrace(eventType))
        {
            if (args is not null && format is not null)
                _writer.WriteLine($"{eventType}: {string.Format(format, args)}");
            else
                _writer.WriteLine($"{eventType}: {format}");
        }
    }

    private bool ShouldTrace(TraceEventType eventType)
    {
        switch (_level)
        {
            case TraceLevel.Off:
                return false;
            case TraceLevel.Error:
                return eventType == TraceEventType.Error || eventType == TraceEventType.Critical;
            case TraceLevel.Warning:
                return eventType == TraceEventType.Warning || eventType == TraceEventType.Error || eventType == TraceEventType.Critical;
            case TraceLevel.Info:
                return eventType != TraceEventType.Verbose; // Info and more severe
            case TraceLevel.Verbose:
                return true; // All events
            default:
                return false;
        }
    }
}

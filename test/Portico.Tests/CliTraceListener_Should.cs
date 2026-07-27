using System.Diagnostics;
using System.IO;
using Xunit;

namespace Portico;

// POR-109. CliTraceListener is the diagnostic surface operators turn on when debugging routing.
// Previously at ~37% coverage — the lowest in the codebase. Every trace level and both TraceEvent
// overloads are exercised, plus the Write/WriteLine direct-write path.
// ReSharper disable once InconsistentNaming
public sealed class CliTraceListener_Should
{
    private static (CliTraceListener listener, StringWriter writer) Create(TraceLevel level)
    {
        var writer = new StringWriter();
        return (new CliTraceListener(level, writer), writer);
    }

    // ---- ShouldTrace level gating ----

    [Theory]
    [InlineData(TraceEventType.Critical)]
    [InlineData(TraceEventType.Error)]
    [InlineData(TraceEventType.Warning)]
    [InlineData(TraceEventType.Information)]
    [InlineData(TraceEventType.Verbose)]
    public void Suppress_all_events_when_level_is_Off(TraceEventType eventType)
    {
        var (listener, writer) = Create(TraceLevel.Off);
        listener.TraceEvent(null, "test", eventType, 0, "should-not-appear");
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData(TraceEventType.Critical, true)]
    [InlineData(TraceEventType.Error, true)]
    [InlineData(TraceEventType.Warning, false)]
    [InlineData(TraceEventType.Information, false)]
    [InlineData(TraceEventType.Verbose, false)]
    public void Gate_events_at_Error_level(TraceEventType eventType, bool shouldAppear)
    {
        var (listener, writer) = Create(TraceLevel.Error);
        listener.TraceEvent(null, "test", eventType, 0, "marker");
        Assert.Equal(shouldAppear, writer.ToString().Contains("marker"));
    }

    [Theory]
    [InlineData(TraceEventType.Critical, true)]
    [InlineData(TraceEventType.Error, true)]
    [InlineData(TraceEventType.Warning, true)]
    [InlineData(TraceEventType.Information, false)]
    [InlineData(TraceEventType.Verbose, false)]
    public void Gate_events_at_Warning_level(TraceEventType eventType, bool shouldAppear)
    {
        var (listener, writer) = Create(TraceLevel.Warning);
        listener.TraceEvent(null, "test", eventType, 0, "marker");
        Assert.Equal(shouldAppear, writer.ToString().Contains("marker"));
    }

    [Theory]
    [InlineData(TraceEventType.Critical, true)]
    [InlineData(TraceEventType.Error, true)]
    [InlineData(TraceEventType.Warning, true)]
    [InlineData(TraceEventType.Information, true)]
    [InlineData(TraceEventType.Verbose, false)]
    public void Gate_events_at_Info_level(TraceEventType eventType, bool shouldAppear)
    {
        var (listener, writer) = Create(TraceLevel.Info);
        listener.TraceEvent(null, "test", eventType, 0, "marker");
        Assert.Equal(shouldAppear, writer.ToString().Contains("marker"));
    }

    [Theory]
    [InlineData(TraceEventType.Critical)]
    [InlineData(TraceEventType.Error)]
    [InlineData(TraceEventType.Warning)]
    [InlineData(TraceEventType.Information)]
    [InlineData(TraceEventType.Verbose)]
    public void Pass_all_events_at_Verbose_level(TraceEventType eventType)
    {
        var (listener, writer) = Create(TraceLevel.Verbose);
        listener.TraceEvent(null, "test", eventType, 0, "marker");
        Assert.Contains("marker", writer.ToString());
    }

    // ---- TraceEvent overloads ----

    [Fact]
    public void Format_message_overload_as_type_colon_message()
    {
        var (listener, writer) = Create(TraceLevel.Verbose);
        listener.TraceEvent(null, "src", TraceEventType.Information, 0, "hello world");
        Assert.Equal($"Information: hello world{writer.NewLine}", writer.ToString());
    }

    [Fact]
    public void Format_args_overload_with_string_format()
    {
        var (listener, writer) = Create(TraceLevel.Verbose);
        listener.TraceEvent(null, "src", TraceEventType.Warning, 0, "count={0}", 42);
        Assert.Equal($"Warning: count=42{writer.NewLine}", writer.ToString());
    }

    [Fact]
    public void Fall_through_to_raw_format_when_args_is_null()
    {
        var (listener, writer) = Create(TraceLevel.Verbose);
        listener.TraceEvent(null, "src", TraceEventType.Error, 0, "raw-message", null);
        Assert.Equal($"Error: raw-message{writer.NewLine}", writer.ToString());
    }

    // ---- Write / WriteLine (treated as Verbose) ----

    [Fact]
    public void Surface_Write_only_at_Verbose_level()
    {
        var (verbose, vw) = Create(TraceLevel.Verbose);
        var (info, iw) = Create(TraceLevel.Info);

        verbose.Write("v-marker");
        info.Write("i-marker");

        Assert.Contains("v-marker", vw.ToString());
        Assert.Empty(iw.ToString());
    }

    [Fact]
    public void Surface_WriteLine_only_at_Verbose_level()
    {
        var (verbose, vw) = Create(TraceLevel.Verbose);
        var (info, iw) = Create(TraceLevel.Info);

        verbose.WriteLine("v-line");
        info.WriteLine("i-line");

        Assert.Contains("v-line", vw.ToString());
        Assert.Empty(iw.ToString());
    }

    // ---- Null message is a no-op ----

    [Fact]
    public void Ignore_null_message_in_Write()
    {
        var (listener, writer) = Create(TraceLevel.Verbose);
        listener.Write(null);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Ignore_null_message_in_WriteLine()
    {
        var (listener, writer) = Create(TraceLevel.Verbose);
        listener.WriteLine(null);
        Assert.Empty(writer.ToString());
    }

    // ---- Redaction integration: sensitive values do not leak through the trace path ----

    [Fact]
    public void Not_leak_sensitive_values_through_trace_plus_timing()
    {
        var console = new StringCliConsole();
        var svc = new SensitiveTraceService();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .UseMiddleware(new CliTracingMiddleware())
            .UseMiddleware(new CliTimingMiddleware())
            .AddCommands(svc));

        var exit = app.Run("app.exe work --secret hunter2 --trace-level Verbose --timing");

        Assert.Equal(0, exit);
        var stderr = console.ErrorWriter.ToString();
        Assert.Contains("[timing]", stderr);
        Assert.Contains("--secret ***", stderr);
        Assert.DoesNotContain("hunter2", stderr);
    }

    public sealed class SensitiveTraceService
    {
        [CliRoute("work")]
        [CliCommandExample("work --secret x")]
        public int Work([CliOption("--secret", Sensitive = true)] string secret)
        {
            Trace.TraceInformation("working");
            return 0;
        }
    }
}

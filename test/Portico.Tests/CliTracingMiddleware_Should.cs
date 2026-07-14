using System.Diagnostics;
using Xunit;

namespace Portico;

// SOL-80: CliTracingMiddleware bridges Trace.* into the injected ICliConsole (not Console.Out),
// gated by --trace-level. Each test uses a unique trace marker so the process-global
// Trace.Listeners state (the documented concurrency caveat) can't make these flaky under parallel
// execution — a marker only ever reaches a listener that is set to surface it.
// ReSharper disable once InconsistentNaming
public sealed class CliTracingMiddleware_Should
{
    public sealed class TracingService
    {
        private readonly string _marker;
        public TracingService(string marker) => _marker = marker;

        [CliRoute("work")]
        [CliCommandExample("work")]
        public int Work()
        {
            Trace.TraceInformation(_marker);
            return 0;
        }
    }

    [Fact]
    public void Route_trace_output_through_the_injected_console()
    {
        const string marker = "sol80-verbose-marker";
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .UseMiddleware(new CliTracingMiddleware())
            .AddCommands(new TracingService(marker)));

        var exit = app.Run("app.exe work --trace-level Verbose");

        Assert.Equal(0, exit);
        Assert.Contains(marker, console.OutWriter.ToString());   // captured via ICliConsole
    }

    [Fact]
    public void Suppress_trace_output_when_level_is_off()
    {
        const string marker = "sol80-off-marker";
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .UseMiddleware(new CliTracingMiddleware())
            .AddCommands(new TracingService(marker)));

        var exit = app.Run("app.exe work");   // --trace-level defaults to Off

        Assert.Equal(0, exit);
        Assert.DoesNotContain(marker, console.OutWriter.ToString());
    }
}

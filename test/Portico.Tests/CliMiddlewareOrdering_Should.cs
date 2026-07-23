using System;
using System.Collections.Generic;
using Xunit;

namespace Portico;

// POR-72. Middleware NESTS, it does not queue. The setup half runs in registration order; the
// unwinding half — OnError then OnActionExecuted — runs in reverse, so a resource the first
// registration acquires is released only after every registration inside it has released its own.
// This is the ASP.NET Core filter contract the CHARTER's metaphor table points at: "the after code
// of filters runs in the reverse order of the before code".
//
// Before POR-72 both halves ran forward, so CliTracingMiddleware could detach its process-global
// Trace.Listeners entry while a later-registered middleware was still writing through it.
// ReSharper disable once InconsistentNaming
public sealed class CliMiddlewareOrdering_Should
{
    public sealed class Recorder
    {
        public List<string> Events { get; } = [];
    }

    public class NamedMiddleware(Recorder recorder, string name) : CliMiddleware
    {
        public override void OnExecutingAction(CliInvocation i) => recorder.Events.Add($"{name}.executing");
        public override void OnError(CliInvocation i, Exception e) => recorder.Events.Add($"{name}.error");
        public override void OnActionExecuted(CliInvocation i) => recorder.Events.Add($"{name}.executed");
    }

    public sealed class MwA(Recorder recorder) : NamedMiddleware(recorder, "a");

    public sealed class MwB(Recorder recorder) : NamedMiddleware(recorder, "b");

    public sealed class MwC(Recorder recorder) : NamedMiddleware(recorder, "c");

    public sealed class Tool(Recorder recorder)
    {
        [CliRoute("ok")]
        [CliCommandExample("ok")]
        public int Ok()
        {
            recorder.Events.Add("handler");
            return 0;
        }

        [CliRoute("boom")]
        [CliCommandExample("boom")]
        public int Boom() => throw new InvalidOperationException("deliberate");
    }

    private static (int ExitCode, List<string> Events) Run(string commandLine)
    {
        var recorder = new Recorder();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(new StringCliConsole())
            .AddCommands(new Tool(recorder))
            .UseMiddleware(new MwA(recorder))
            .UseMiddleware(new MwB(recorder))
            .UseMiddleware(new MwC(recorder)));

        return (app.Run(commandLine), recorder.Events);
    }

    [Fact]
    public void Unwind_OnActionExecuted_In_Reverse_Registration_Order()
    {
        var (exitCode, events) = Run("app ok");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "a.executing", "b.executing", "c.executing",
                "handler",
                "c.executed", "b.executed", "a.executed"
            ],
            events);
    }

    [Fact]
    public void Unwind_OnError_And_OnActionExecuted_In_Reverse_When_The_Handler_Throws()
    {
        var (exitCode, events) = Run("app boom");

        Assert.Equal(CliExitException.RuntimeErrorExitCode, exitCode);
        Assert.Equal(
            [
                "a.executing", "b.executing", "c.executing",
                // OnError and OnActionExecuted are both the unwinding half, so they agree.
                "c.error", "b.error", "a.error",
                "c.executed", "b.executed", "a.executed"
            ],
            events);
    }

    public sealed class ThrowingMiddleware(Recorder recorder) : CliMiddleware
    {
        public override void OnExecutingAction(CliInvocation i)
        {
            recorder.Events.Add("thrower.executing");
            throw new InvalidOperationException("setup failed");
        }

        public override void OnActionExecuted(CliInvocation i) => recorder.Events.Add("thrower.executed");
    }

    [Fact]
    public void Still_Run_Every_OnActionExecuted_When_A_Setup_Hook_Throws()
    {
        // The middleware that never got to set up must still be torn down: OnActionExecuted is armed
        // before the setup walk precisely so a half-built pipeline still releases what it acquired.
        var recorder = new Recorder();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(new StringCliConsole())
            .AddCommands(new Tool(recorder))
            .UseMiddleware(new MwA(recorder))
            .UseMiddleware(new ThrowingMiddleware(recorder))
            .UseMiddleware(new MwC(recorder)));

        app.Run("app ok");

        Assert.Equal(
            [
                "a.executing", "thrower.executing",
                // A setup failure is a failure like any other, so it reaches OnError too — in reverse,
                // and skipping the thrower, which does not override the hook.
                "c.error", "a.error",
                // 'c' never set up — its setup hook was never reached — but every registration is
                // still torn down, in reverse.
                "c.executed", "thrower.executed", "a.executed"
            ],
            recorder.Events);
    }
}

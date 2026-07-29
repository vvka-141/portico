using System;
using System.Diagnostics;
using System.Text;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-120. A route may declare `--help` or `-h` as one of its own option aliases, and when it does
// the route wins — that precedence is SOL-75 and it is what makes `-h` mean `--host`, which is a
// convention no framework should fight.
//
// The consequence was unacknowledged in both directions: the author got no signal that a command's
// help had become unreachable, and a user typing `--help` got "cannot be used as a flag", which
// reads as a fault in the tool rather than as a consequence of the contract.
//
// Dispatch is unchanged. Only the two silences are closed.
public sealed class CliShadowedBuiltInTrigger_Should
{
    public sealed class ShadowsHelpOption
    {
        [CliRoute("run")]
        [CliCommandExample("run --help x")]
        public int Run([CliOption("--help")] string help = "") => 0;
    }

    public sealed class ShadowsShortHelp
    {
        public string? Host;

        [CliRoute("run")]
        [CliCommandExample("run -h db")]
        public int Run([CliOption("--host|-h")] string host = "")
        {
            Host = host;
            return 0;
        }
    }

    public sealed class ShadowsVersion
    {
        [CliRoute("run")]
        [CliCommandExample("run -V 2")]
        public int Run([CliOption("-V")] int v = 0) => 0;
    }

    public sealed class ShadowsNothing
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run([CliOption("--name")] string name = "") => 0;
    }

    private static string TraceOf(Action build)
    {
        var captured = new StringBuilder();
        var listener = new CapturingTraceListener(captured);
        Trace.Listeners.Add(listener);
        try
        {
            build();
            return captured.ToString();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    private sealed class CapturingTraceListener(StringBuilder sink) : TraceListener
    {
        public override void Write(string? message) => sink.Append(message);
        public override void WriteLine(string? message) => sink.AppendLine(message);
    }

    private static string TraceOfCreating<T>() where T : class, new() =>
        TraceOf(() => CliApplication.Create(cfg => cfg
            .WithConsole(new StringCliConsole())
            .AddCommands<T>(() => new T())));

    // --- AC 1: the author learns before a user does -------------------------------------------

    [Fact]
    public void Warn_The_Author_That_A_Route_Shadowed_Help()
    {
        var traced = TraceOfCreating<ShadowsHelpOption>();

        Assert.Contains("Route 'run'", traced, StringComparison.Ordinal);
        Assert.Contains("'--help'", traced, StringComparison.Ordinal);
        Assert.Contains("built-in help", traced, StringComparison.Ordinal);
    }

    [Fact]
    public void Warn_The_Author_That_A_Route_Shadowed_Version()
    {
        var traced = TraceOfCreating<ShadowsVersion>();

        Assert.Contains("'-V'", traced, StringComparison.Ordinal);
        Assert.Contains("built-in version", traced, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message must not overclaim. Shadowing <c>-h</c> leaves <c>--help</c> working, and saying
    /// "help is unreachable" there would be the same kind of misleading message this ticket exists
    /// to remove.
    /// </summary>
    [Fact]
    public void Name_Only_The_Triggers_Actually_Shadowed()
    {
        var shortForm = TraceOfCreating<ShadowsShortHelp>();
        Assert.Contains("'-h'", shortForm, StringComparison.Ordinal);
        Assert.Contains("'--help'", shortForm, StringComparison.Ordinal);
        Assert.Contains("still work", shortForm, StringComparison.Ordinal);
        Assert.DoesNotContain("no remaining way", shortForm, StringComparison.Ordinal);
    }

    [Fact]
    public void Stay_Quiet_When_No_Trigger_Is_Shadowed()
    {
        var traced = TraceOfCreating<ShadowsNothing>();

        Assert.DoesNotContain("built-in help", traced, StringComparison.Ordinal);
        Assert.DoesNotContain("built-in version", traced, StringComparison.Ordinal);
    }

    /// <summary>
    /// Measured against the <em>effective</em> triggers, not the defaults. An application that
    /// replaced them is judged against its own set — which is the reason this is a runtime check
    /// rather than an analyzer.
    /// </summary>
    [Fact]
    public void Measure_Against_Custom_Triggers_When_They_Are_Configured()
    {
        var traced = TraceOf(() => CliApplication.Create(cfg => cfg
            .WithConsole(new StringCliConsole())
            .WithHelp(help => help.Triggers("--usage"))
            .AddCommands(new ShadowsHelpOption())));

        // '--help' is no longer a trigger for this application, so shadowing it is not a shadow.
        Assert.DoesNotContain("built-in help", traced, StringComparison.Ordinal);
    }

    // --- AC 3: the user's message no longer reads as a framework fault ------------------------

    [Fact]
    public void Explain_The_Shadowing_In_The_Runtime_Error()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new ShadowsHelpOption()))
            .Run("app run --help");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("declares '--help' as one of its own options", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("does not answer", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>An ordinary option that is not a trigger keeps the plain message — no noise.</summary>
    [Fact]
    public void Leave_An_Ordinary_Options_Message_Alone()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new ShadowsNothing()))
            .Run("app run --name");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("cannot be used as a flag", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("its own options", result.StandardError, StringComparison.Ordinal);
    }

    // --- AC 4: SOL-75 behaviour is unchanged ---------------------------------------------------

    [Fact]
    public void Still_Let_A_Route_Use_Short_h_For_Its_Own_Option()
    {
        var tool = new ShadowsShortHelp();

        CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(tool))
            .Run("app run -h db")
            .ExpectExit(0);

        Assert.Equal("db", tool.Host);
    }

    [Fact]
    public void Still_Answer_The_Unshadowed_Trigger()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new ShadowsShortHelp()))
            .Run("app run --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.StandardOut, StringComparison.Ordinal);
    }
}

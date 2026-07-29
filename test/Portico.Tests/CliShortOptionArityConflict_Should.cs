using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-119. Short-option bundling on one command stops working because a DIFFERENT command reused the
// same letter with a different arity. The affected command is never touched.
//
// The degradation itself is correct and stays: expansion runs on raw argv before any route has
// matched, so there is no per-route schema to consult, and the expander refuses to guess how to
// split `-fx` when `-f` might take a value. The defect was that it was SILENT — an author had no way
// to learn that registering one command degraded another.
//
// It is a trace warning rather than a CliConfigurationException on purpose. Two independently-built
// tools composed into one binary may each legitimately use `-f` with a different arity, and that is
// the composition docs/how-to/compose-clis.md promotes; throwing would fail a program that works, to
// prevent a degradation the user resolves by typing `-f -x`.
public sealed class CliShortOptionArityConflict_Should
{
    public sealed class FlagTool
    {
        public bool Ran;

        [CliRoute("a")]
        [CliCommandExample("a")]
        public int A([CliOption("-f")] CliFlag? f = null, [CliOption("-x")] CliFlag? x = null)
        {
            Ran = f is not null && x is not null;
            return 0;
        }
    }

    public sealed class ScalarTool
    {
        [CliRoute("b")]
        [CliCommandExample("b")]
        public int B([CliOption("-f")] string f = "") => 0;
    }

    /// <summary>
    /// Captures what the framework traced while the application was being built. The suite runs
    /// serially (<c>DisableTestParallelization</c>), so swapping a process-global listener is safe —
    /// the same reason <c>CliTestHarness</c> can swap the console.
    /// </summary>
    private static (T Result, string Trace) WhileTracing<T>(Func<T> build)
    {
        var captured = new StringBuilder();
        var listener = new CapturingTraceListener(captured);
        Trace.Listeners.Add(listener);
        try
        {
            return (build(), captured.ToString());
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

    [Fact]
    public void Report_The_Conflict_At_Create_Naming_Both_Routes_And_The_Letter()
    {
        var (_, traced) = WhileTracing(() =>
            CliApplication.Create(cfg => cfg
                .WithConsole(new StringCliConsole())
                .AddCommands(new FlagTool())
                .AddCommands(new ScalarTool())));

        Assert.Contains("'-f'", traced, StringComparison.Ordinal);
        Assert.Contains("route 'a'", traced, StringComparison.Ordinal);
        Assert.Contains("route 'b'", traced, StringComparison.Ordinal);
        Assert.Contains("flag", traced, StringComparison.Ordinal);
        Assert.Contains("scalar", traced, StringComparison.Ordinal);

        // The consequence and the repair, because a warning that only says "there is a conflict"
        // leaves the author exactly where they started.
        Assert.Contains("bundling is disabled", traced, StringComparison.Ordinal);
        Assert.Contains("different letter", traced, StringComparison.Ordinal);
    }

    [Fact]
    public void Stay_Quiet_When_Every_Route_Agrees()
    {
        var (_, traced) = WhileTracing(() =>
            CliApplication.Create(cfg => cfg
                .WithConsole(new StringCliConsole())
                .AddCommands(new FlagTool())));

        Assert.DoesNotContain("bundling is disabled", traced, StringComparison.Ordinal);
    }

    /// <summary>
    /// The degradation, pinned. This behaviour was undocumented and untested; it is deliberate, and
    /// it must not change by accident — but it must also not be discovered by a user again.
    /// </summary>
    [Fact]
    public void Disable_Bundling_For_A_Conflicted_Letter_Across_The_Whole_Application()
    {
        var alone = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new FlagTool()))
            .Run("app a -fx");

        Assert.Equal(0, alone.ExitCode);

        var composed = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new FlagTool()).AddCommands(new ScalarTool()))
            .Run("app a -fx");

        // Route 'a' is untouched by the conflict and still loses bundling — that is the coupling.
        Assert.Equal(CliExitException.UsageErrorExitCode, composed.ExitCode);
        Assert.Contains("Did you mean: -f, -x?", composed.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only the glued form degrades. Both commands keep working written out, which is why this is a
    /// warning rather than a configuration error.
    /// </summary>
    [Fact]
    public void Keep_Both_Commands_Working_Unbundled()
    {
        var tool = new FlagTool();

        CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(tool).AddCommands(new ScalarTool()))
            .Run("app a -f -x")
            .ExpectExit(0);

        Assert.True(tool.Ran);

        CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new FlagTool()).AddCommands(new ScalarTool()))
            .Run("app b -f hello")
            .ExpectExit(0);
    }

    /// <summary>
    /// A composed application does not throw. Two independent tools using <c>-f</c> differently is
    /// legal, and `compose-clis.md` promotes exactly that shape.
    /// </summary>
    [Fact]
    public void Not_Refuse_To_Build_A_Composed_Application()
    {
        var exception = Record.Exception(() =>
            CliApplication.Create(cfg => cfg
                .WithConsole(new StringCliConsole())
                .AddCommands(new FlagTool())
                .AddCommands(new ScalarTool())));

        Assert.Null(exception);
    }

    [Fact]
    public void Expose_The_Conflicting_Letters_On_The_Schema()
    {
        var schema = new CliShortOptionSchema(
            new Dictionary<char, CliShortOptionArity>(),
            new[] { 'f' });

        Assert.Contains('f', schema.ConflictingShortNames);
        Assert.Empty(CliShortOptionSchema.Empty.ConflictingShortNames);
    }
}

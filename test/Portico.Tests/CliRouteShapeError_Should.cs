using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-115. When a route's literal prefix matches but the segment count is wrong, the error
// should name the route and the expected vs supplied argument count instead of "Unknown command".
// When an option-looking token ate a positional, the `--` terminator should be mentioned.
public sealed class CliRouteShapeError_Should
{
    public interface ITool
    {
        [CliRoute("greet {name}")]
        [CliCommandExample("greet Alice")]
        int Greet(string name);

        [CliRoute("copy {src} {dst}")]
        [CliCommandExample("copy a b")]
        int Copy(string src, string dst);
    }

    private sealed class Tool : ITool
    {
        public int Greet(string name) => 0;
        public int Copy(string src, string dst) => 0;
    }

    private static CliTestHarness Harness() =>
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(new Tool()));

    // --- AC1: argument-count mismatch ---

    [Fact]
    public void Report_Argument_Count_When_Too_Few_Are_Supplied()
    {
        var result = Harness().Run("app copy a");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("copy {src} {dst}", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("2", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("got 1", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown command", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_Argument_Count_When_Too_Many_Are_Supplied()
    {
        var result = Harness().Run("app greet Alice Bob");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("greet {name}", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("got 2", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown command", result.StandardError, StringComparison.Ordinal);
    }

    // --- AC2: option-looking token ate a positional ---

    [Fact]
    public void Mention_Terminator_When_Positional_Looks_Like_An_Option()
    {
        var result = Harness().Run("app greet -bob");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("greet {name}", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("--", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("Unknown command", result.StandardError, StringComparison.Ordinal);
    }

    // --- AC3: don't suggest a route the user already matched ---

    [Fact]
    public void Not_Suggest_The_Matched_Route_Back()
    {
        var result = Harness().Run("app copy a");

        Assert.DoesNotContain("Did you mean", result.StandardError, StringComparison.Ordinal);
    }

    // --- AC4: option values are never echoed (POR-48 regression) ---

    [Fact]
    public void Not_Echo_Option_Values_On_Shape_Mismatch()
    {
        var result = Harness().Run("app greet --secret hunter2");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.DoesNotContain("hunter2", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.StandardOut, StringComparison.Ordinal);
    }
}

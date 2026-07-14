using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-37. `[CliOption] TimeSpan` accepted "30 seconds"; `[CliOption] TimeSpan?` did not.
//
// CliOptionAttribute.CanAccept tested `optionType == typeof(TimeSpan)`, and Nullable<TimeSpan> is
// not typeof(TimeSpan) — so the nullable form silently kept the BCL converter, which parses only
// "00:00:30". TimeSpan? is precisely the form an OPTIONAL timeout takes, so the feature that makes
// a duration usable from a terminal was missing from the case that needs it most.
//
// Found by CliContractValidator when the worked example (examples/AdminCli) declared
// [CliCommandExample("drain --timeout \"30 seconds\"")] — the example failed to dispatch.
public sealed class CliTimeSpanOption_Should
{
    public interface ITool
    {
        [CliRoute("drain")]
        [CliCommandExample("drain --timeout \"30 seconds\"")]
        int Drain([CliOption("--timeout")] TimeSpan? timeout = null);

        [CliRoute("wait")]
        [CliCommandExample("wait --for \"30 seconds\"")]
        int Wait([CliOption("--for")] TimeSpan duration = default);
    }

    private sealed class Tool : ITool
    {
        public int Drain(TimeSpan? timeout)
        {
            Console.WriteLine(timeout is null ? "none" : $"{timeout.Value.TotalSeconds:0.###}");
            return 0;
        }

        public int Wait(TimeSpan duration)
        {
            Console.WriteLine($"{duration.TotalSeconds:0.###}");
            return 0;
        }
    }

    private static CliTestRunResult Run(string commandLine) =>
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(new Tool())).Run(commandLine);

    [Theory]
    [InlineData("30 seconds", "30")]
    [InlineData("5 min", "300")]
    [InlineData("1.5 hours", "5400")]
    [InlineData("PT30S", "30")]        // ISO 8601
    [InlineData("00:00:30", "30")]     // the .NET format — must not regress
    public void Accept_Every_Format_On_The_NULLABLE_Form(string value, string expectedSeconds)
    {
        var result = Run($"app.exe drain --timeout \"{value}\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expectedSeconds, result.StandardOut, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("30 seconds", "30")]
    [InlineData("5 min", "300")]
    [InlineData("1.5 hours", "5400")]
    [InlineData("PT30S", "30")]
    [InlineData("00:00:30", "30")]
    public void Accept_Every_Format_On_The_NonNullable_Form(string value, string expectedSeconds)
    {
        // The form that already worked — asserted so the fix cannot regress it.
        var result = Run($"app.exe wait --for \"{value}\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expectedSeconds, result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_Null_When_The_Optional_TimeSpan_Is_Absent()
    {
        var result = Run("app.exe drain");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("none", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_A_Malformed_Duration()
    {
        var result = Run("app.exe drain --timeout \"not a duration\"");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
    }

    [Fact]
    public void Dispatch_Every_Declared_Example()
    {
        var notDispatched = 0;
        new CliContractValidator<ITool>().Validate(onNotInvoked: (_, _) => notDispatched++);

        Assert.Equal(0, notDispatched);
    }
}

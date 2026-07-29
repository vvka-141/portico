using System;
using System.Globalization;
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

    // POR-147 from here down.

    /// <summary>
    /// `--timeout 30` bound thirty days. That is BCL <c>TimeSpan.Parse</c> behaviour and every
    /// surveyed .NET CLI framework inherits it — but Portico is the one that already promised to
    /// understand "30 seconds", so a user who learned that reads "30" as seconds. It was the only
    /// value in the converter that failed <em>silently</em>, and on a drain or a migrate that is an
    /// outage rather than a typo.
    /// </summary>
    [Theory]
    [InlineData("30")]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData(" 30 ")]
    public void Refuse_A_Bare_Number_Rather_Than_Binding_Days(string value)
    {
        var result = Run($"app.exe drain --timeout \"{value}\"");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("means DAYS", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// Refused, never reinterpreted. Reading a bare number as seconds would be friendlier and is the
    /// wrong call: the same string would mean one thing in Portico and another in every other .NET
    /// tool. This test exists to make that a decision someone has to argue with, not one a
    /// convenience patch can quietly reverse.
    /// </summary>
    [Fact]
    public void Name_Both_Repairs_When_It_Refuses_A_Bare_Number()
    {
        var result = Run("app.exe drain --timeout 30");

        Assert.Contains("'30s'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'30 seconds'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'00:00:30'", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("00:00:30\n", result.StandardOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// The compact forms operators actually type — Go durations, <c>kubectl --timeout</c>, systemd,
    /// Prometheus. They were rejected; the reason was a single mandatory <c>\s+</c> between number
    /// and unit, not a missing unit table.
    /// </summary>
    [Theory]
    [InlineData("90s", "90")]
    [InlineData("1h30m", "5400")]
    [InlineData("500ms", "0.5")]
    [InlineData("2d", "172800")]
    [InlineData("0.5d", "43200")]
    [InlineData("1H30M", "5400")]        // case-insensitive
    [InlineData("1h 30m", "5400")]       // mixed spacing
    [InlineData("500 ms", "0.5")]
    [InlineData("2 days 4 hrs", "187200")]
    [InlineData("30 sec", "30")]
    public void Accept_The_Compact_And_Spelled_Out_Forms(string value, string expectedSeconds)
    {
        var result = Run($"app.exe drain --timeout \"{value}\"");

        // The handler renders with "0.###" under the ambient culture, so a fractional expectation
        // written "0.5" here would be "0,5" on this machine. Format the expected value the same way
        // rather than pinning the suite to one decimal separator.
        var expected = double.Parse(expectedSeconds, CultureInfo.InvariantCulture)
            .ToString("0.###", CultureInfo.CurrentCulture);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(expected, result.StandardOut, StringComparison.Ordinal);
    }

    /// <summary>
    /// A message that restates the input and names none of the four things that would have worked is
    /// not a diagnosis. Every non-ISO-8601 failure lands on this text, so it is the one a mistyped
    /// duration actually gets.
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("3 apples")]
    [InlineData("30 seconds 5")]     // trailing bare number — must not be silently ignored
    [InlineData("999999999999d")]    // parses, then overflows TimeSpan
    public void Name_The_Accepted_Forms_When_It_Rejects(string value)
    {
        var result = Run($"app.exe drain --timeout \"{value}\"");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("'90s'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'30 seconds'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("'PT30S'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("ms, s, m, h, d", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// The unit aliases used to be applied by rewriting the input before matching, and two of the
    /// four patterns were ungrouped: <c>\bminutes?|mins?\b</c> parses as
    /// <c>(\bminutes?)|(mins?\b)</c>, whose second alternative has no leading word boundary — so
    /// <c>admin</c> normalised to <c>adminutes</c>. It was unreachable only because the mandatory
    /// whitespace between number and unit stopped such inputs before the rewrite, which is the exact
    /// requirement POR-147 relaxes. The rewrite pass is gone rather than patched; these inputs pin
    /// that it stays gone.
    /// </summary>
    [Theory]
    [InlineData("admin")]
    [InlineData("5 admin")]
    [InlineData("1 short")]
    [InlineData("2 amsterdam")]
    public void Not_Mistake_A_Unit_Alias_Buried_Inside_A_Word(string value)
    {
        var result = Run($"app.exe drain --timeout \"{value}\"");

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

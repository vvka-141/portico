using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-143. Found by making the mistake a newcomer makes, against the published 0.1.1 packages.
//
//     [CliOption("--dry-run")] bool dryRun = false      // then: `db migrate prod --dry-run`
//
// answered "The option '--dry-run' cannot be used as a flag. Provide a single value instead."
//
// That diagnoses the INVOCATION and prescribes `--dry-run true`. It is not wrong — a bool option
// really does take a value — but it is the wrong half of the problem to fix, and `--dry-run true`
// is not how CLIs are written. The framework already has the right answer, and the scaffold says so
// in a comment the author is not looking at when the error fires. Someone who trusted the message
// shipped `--dry-run true` and never learned CliFlag existed.
//
// The mistake was always CAUGHT — CliContractValidator<T> reported it per example, which is the
// wedge working. Only the reason pointed the wrong way.
// ReSharper disable once InconsistentNaming
public sealed class CliFlagCaptureMessage_Should
{
    public sealed class MigrateTool
    {
        [CliRoute("db migrate {target}")]
        [CliCommandExample("db migrate prod --dry-run", "Show what would run")]
        public int Migrate(
            [CliArgument("target")] string target,
            [CliOption("--dry-run")] bool dryRun = false) => 0;

        [CliRoute("connect")]
        [CliCommandExample("connect --host db1")]
        public int Connect([CliOption("--host")] string host = "") => 0;

        [CliRoute("retry")]
        [CliCommandExample("retry --times 3")]
        public int Retry([CliOption("--times")] int times = 1) => 0;

        [CliRoute("verbose")]
        [CliCommandExample("verbose --loud true")]
        public int Verbose([CliOption("--loud")] bool? loud = null) => 0;
    }

    private static CliTestRunResult Run(string commandLine) =>
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(new MigrateTool())).Run(commandLine);

    /// <summary>The ticket's own repro: the fix named must be the declaration, not the invocation.</summary>
    [Theory]
    [InlineData("app db migrate prod --dry-run", "--dry-run")]
    [InlineData("app verbose --loud", "--loud")]          // bool? reaches the same advice
    public void Point_A_Bool_Option_At_CliFlag(string commandLine, string option)
    {
        var result = Run(commandLine);

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("declare the parameter as 'CliFlag?'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains($"'{option}', not '{option} true'", result.StandardError, StringComparison.Ordinal);

        // The value form stays on the table — a genuine two-state option is legitimate, which is
        // exactly why POR012 is a Warning and not an Error.
        Assert.Contains($"pass '{option} true'", result.StandardError, StringComparison.Ordinal);

        // The compile-time half of the same advice, cited the way the POR010 message cites its rule.
        Assert.Contains("POR012", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// The declared type is named, because that is the thing the author has to go and look at.
    /// </summary>
    [Theory]
    [InlineData("app db migrate prod --dry-run", "bool")]
    [InlineData("app connect --host", "string")]
    [InlineData("app retry --times", "int")]
    [InlineData("app verbose --loud", "bool?")]
    public void Name_The_Declared_Type(string commandLine, string declared)
    {
        var result = Run(commandLine);

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains($"its declared type is '{declared}'", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// A non-bool option must NOT be told to declare <c>CliFlag?</c>.
    /// </summary>
    /// <remarks>
    /// This is the case that keeps the fix from being a different wrong prescription. For a
    /// <c>string</c> or an <c>int</c> there is no flag form to reach for — the author simply left the
    /// value off — and the old advice was right for them all along. A blanket reword would have
    /// pointed every one of these at a type that cannot hold their value.
    /// </remarks>
    [Theory]
    [InlineData("app connect --host", "--host <string>")]
    [InlineData("app retry --times", "--times <int>")]
    public void Ask_A_Value_Option_For_Its_Value(string commandLine, string hint)
    {
        var result = Run(commandLine);

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains(hint, result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("CliFlag", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("POR012", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// A `Sensitive` option's message carries no user input, and this path never echoed any — the
    /// option name and the declared type both come from the declaration. Pinned so a future edit
    /// that adds the received token has to notice.
    /// </summary>
    [Fact]
    public void Echo_Nothing_The_User_Typed()
    {
        var result = Run("app connect --host");

        Assert.DoesNotContain("connect --host", result.StandardError, StringComparison.Ordinal);
    }
}

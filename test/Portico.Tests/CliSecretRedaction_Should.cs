using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-24. Option values reached stderr verbatim on the DEFAULT path — a route typo printed
// --connection-string and --token into the log. In a container stderr IS the log stream.
//
// Two vectors, two different fixes, because they have different information available:
//   * Unknown command -> no route matched, so no option metadata exists and the framework CANNOT
//     know which values are secret. It therefore renders no option values at all.
//   * Route matched   -> metadata exists, so values of [CliOption(Sensitive = true)] render as ***.
public sealed class CliSecretRedaction_Should
{
    private const string Password = "Host=db;Password=hunter2";
    private const string Token = "sk-live-SECRET";

    public interface IAdmin
    {
        [CliRoute("db migrate")]
        [CliCommandExample("db migrate --connection-string \"Host=db\"")]
        int Migrate(
            [CliOption("--connection-string", Sensitive = true)] string connectionString,
            [CliOption("--token", Sensitive = true)] string? token = null,
            [CliOption("--verbose")] CliFlag? verbose = null);

        [CliRoute("db seed")]
        [CliCommandExample("db seed --rows 10")]
        int Seed([CliOption("--rows", Sensitive = true)] int rows);
    }

    private sealed class Admin : IAdmin
    {
        public int Migrate(string connectionString, string? token, CliFlag? verbose) => 0;
        public int Seed(int rows) => 0;
    }

    private static CliTestHarness Harness() =>
        CliTestHarness.ForApplication(cfg => cfg
            .AddCommands(new Admin())
            .UseMiddleware(new CliTimingMiddleware()));

    [Fact]
    public void Not_Echo_Option_Values_When_The_Route_Is_Mistyped()
    {
        // 'migrat' — an ordinary typo. No middleware needed to reach this path.
        var result = Harness().Run(
            $"admin.exe db migrat --connection-string \"{Password}\" --token \"{Token}\"");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.DoesNotContain("hunter2", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.StandardOut, StringComparison.Ordinal);

        // The diagnostic still has to be useful: it names what was typed and suggests the route.
        Assert.Contains("Unknown command", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("db migrat", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("db migrate", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_Sensitive_Values_In_Timing_Output()
    {
        var result = Harness().Run(
            $"admin.exe db migrate --connection-string \"{Password}\" --token \"{Token}\" --timing");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("hunter2", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, result.StandardError, StringComparison.Ordinal);

        // The middleware still reports — it just reports the option NAMES and a placeholder.
        Assert.Contains("[timing]", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("--connection-string ***", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("--token ***", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Leave_Non_Sensitive_Options_Untouched()
    {
        // Redaction is opt-in. A flag carries no value and must still render normally.
        var result = Harness().Run(
            $"admin.exe db migrate --connection-string \"{Password}\" --verbose --timing");

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("hunter2", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("--verbose", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_Echo_A_Sensitive_Value_That_Fails_Conversion()
    {
        // The conversion failure must not leak the value — nor the inner exception's message,
        // which embeds the value too ("'hunter2' is not a valid value for Int32").
        var result = Harness().Run("admin.exe db seed --rows hunter2");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.DoesNotContain("hunter2", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("invalid", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-16. `-v` used to set BOTH -v and -V, silently, with exit 0 — the worst class of CLI bug:
// a successful exit code and wrong state.
//
// Root cause: two comparers disagreed. The duplicate-alias guard (CliMethodInfo) used
// StringComparer.Ordinal, so `-v` and `-V` looked like distinct options and passed validation;
// the matcher (CliOptionSpec) used OrdinalIgnoreCase, so each token then matched BOTH of them.
//
// The invariant — already implemented and documented for the framework's own help/version
// triggers, just never applied to user options — is now in ONE place, CliAliasComparer:
//   single-char short aliases (-v) are case-SENSITIVE; longer forms are case-INSENSITIVE.
public sealed class CliAliasCase_Should
{
    public interface ITool
    {
        // The POSIX idiom this bug made inexpressible: curl -v (verbose) vs curl -V (version).
        [CliRoute("run")]
        [CliCommandExample("run -v")]
        int Run(
            [CliOption("--verbose|-v")] CliFlag? verbose = null,
            [CliOption("--version-info|-V")] CliFlag? versionInfo = null,
            [CliOption("--force|-f")] CliFlag? force = null);
    }

    private sealed class Tool : ITool
    {
        public int Run(CliFlag? verbose, CliFlag? versionInfo, CliFlag? force)
        {
            Console.WriteLine($"v={verbose is not null} V={versionInfo is not null} f={force is not null}");
            return 0;
        }
    }

    private static CliTestRunResult Run(string commandLine) =>
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(new Tool())).Run(commandLine);

    [Fact]
    public void Treat_Lowercase_And_Uppercase_Short_Aliases_As_Different_Options()
    {
        var result = Run("app.exe run -v");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("v=True V=False", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_The_Uppercase_Short_Alias_On_Its_Own()
    {
        var result = Run("app.exe run -V");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("v=False V=True", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Bind_Both_Only_When_Both_Are_Passed()
    {
        var result = Run("app.exe run -v -V");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("v=True V=True", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_A_Glued_Short_Run_Case_Sensitively()
    {
        // POSIX gluing must respect the same rule: -vV is -v AND -V, not -v twice.
        var result = Run("app.exe run -vV");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("v=True V=True", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_Long_Aliases_Case_Insensitive()
    {
        // Long forms stay forgiving — this is the behavior CliOptionSpec_Should already asserts.
        var result = Run("app.exe run --VERBOSE");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("v=True V=False", result.StandardOut, StringComparison.Ordinal);
    }

    [Fact]
    public void Still_Reject_A_Genuinely_Duplicated_Alias()
    {
        // The duplicate-alias guard must keep working: same alias, same case, two parameters.
        var exception = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new Duplicated())));

        Assert.Contains("--name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reject_Long_Aliases_That_Differ_Only_By_Case()
    {
        // Long forms are case-insensitive, so --name and --NAME ARE the same option — declaring
        // both is a duplicate and must be rejected, not silently double-bound.
        var exception = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new CaseDuplicated())));

        Assert.Contains("--NAME", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public interface IDuplicated
    {
        [CliRoute("a")]
        [CliCommandExample("a --name x")]
        int A([CliOption("--name")] string first, [CliOption("--name")] string second);
    }

    private sealed class Duplicated : IDuplicated
    {
        public int A(string first, string second) => 0;
    }

    public interface ICaseDuplicated
    {
        [CliRoute("b")]
        [CliCommandExample("b --name x")]
        int B([CliOption("--name")] string first, [CliOption("--NAME")] string second);
    }

    private sealed class CaseDuplicated : ICaseDuplicated
    {
        public int B(string first, string second) => 0;
    }
}

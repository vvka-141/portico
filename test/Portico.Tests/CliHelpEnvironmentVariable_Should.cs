using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-149. `EnvironmentVariable` was honoured by the binder and invisible to `--help`. For the
// audience — a containerized backend service — `--help` is frequently the ONLY surface an operator
// has: they did not write the tool, they have no checkout, and they are looking at a pod that is
// failing. An option fed by APP_PASSWORD whose help says nothing is an option they cannot operate.
//
// Rendering it is not novel; CliFx does it. What stopped the rest of the ecosystem is the leak —
// dotnet/command-line-api#1191, open since 2021: "The environment variable is expanded and shown in
// the help. This might be an issue if the environment variable is being used on sensitive options."
// Nobody shipped a fix in five years. The whole point of this suite is the second half of the
// feature: the NAME is shown, the VALUE never is.
public sealed class CliHelpEnvironmentVariable_Should
{
    private const string SecretValue = "s3cr3t-sentinel-value";
    private const string HostValue = "host-sentinel-value";

    public sealed class Bundle : CliOptions
    {
        [CliOption("--region", "Target region", EnvironmentVariable = "POR_H_REGION")]
        public string Region { get; set; } = "eu";
    }

    public sealed class Tool
    {
        [CliRoute("deploy")]
        [CliCommandExample("deploy")]
        public int Deploy(
            [CliOption("--password", "Service password", EnvironmentVariable = "POR_H_PASSWORD", Sensitive = true)]
            string? password = null,
            [CliOption("--host", "Target host", EnvironmentVariable = "POR_H_HOST")]
            string host = "localhost",
            [CliOption("--plain", "Declares no variable")]
            string plain = "x",
            Bundle? bundle = null) => 0;
    }

    /// <summary>
    /// Renders <c>--help</c> with the sentinel values actually set, so an assertion that a value did
    /// not leak is a real assertion rather than a statement about an unset variable.
    /// </summary>
    private static string Help()
    {
        Environment.SetEnvironmentVariable("POR_H_PASSWORD", SecretValue);
        Environment.SetEnvironmentVariable("POR_H_HOST", HostValue);
        Environment.SetEnvironmentVariable("POR_H_REGION", "region-sentinel-value");
        try
        {
            var result = CliTestHarness
                .ForApplication(cfg => cfg.AddCommands(new Tool()))
                .Run("app deploy --help");

            Assert.Equal(0, result.ExitCode);
            return result.StandardOut;
        }
        finally
        {
            Environment.SetEnvironmentVariable("POR_H_PASSWORD", null);
            Environment.SetEnvironmentVariable("POR_H_HOST", null);
            Environment.SetEnvironmentVariable("POR_H_REGION", null);
        }
    }

    [Fact]
    public void Name_The_Variable_Every_Option_Falls_Back_To()
    {
        var help = Help();

        Assert.Contains("(env: POR_H_PASSWORD)", help, StringComparison.Ordinal);
        Assert.Contains("(env: POR_H_HOST)", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole reason the ecosystem left this unshipped. The value is never read on the help path,
    /// so there is nothing to redact — but "nothing to redact" is an invariant, and an invariant
    /// nobody asserts is one a later refactor can quietly reverse.
    /// </summary>
    [Fact]
    public void Never_Render_A_Variables_Value()
    {
        var help = Help();

        Assert.DoesNotContain(SecretValue, help, StringComparison.Ordinal);
        Assert.DoesNotContain(HostValue, help, StringComparison.Ordinal);
        Assert.DoesNotContain("region-sentinel-value", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// A sensitive env-backed option is the container-secrets case this feature exists for, and the
    /// combination most likely to be rendered wrongly. The variable NAME is a declaration in source
    /// and is exactly what the operator needs; the default still redacts to <c>***</c>.
    /// </summary>
    [Fact]
    public void Show_The_Variable_Name_On_A_Sensitive_Option()
    {
        var help = Help();

        Assert.Contains("(env: POR_H_PASSWORD)", help, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretValue, help, StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_Redacting_A_Sensitive_Default()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new SensitiveDefaultTool()))
            .Run("app run --help");

        Assert.Contains("(default: ***)", result.StandardOut, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", result.StandardOut, StringComparison.Ordinal);
        Assert.Contains("(env: POR_H_TOKEN)", result.StandardOut, StringComparison.Ordinal);
    }

    public sealed class SensitiveDefaultTool
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--token", "API token", EnvironmentVariable = "POR_H_TOKEN", Sensitive = true)]
            string token = "hunter2") => 0;
    }

    /// <summary>An option declaring no variable renders exactly as it did before.</summary>
    [Fact]
    public void Leave_An_Option_With_No_Variable_Unchanged()
    {
        var plainRow = Assert.Single(
            Help().Split('\n'),
            line => line.Contains("--plain", StringComparison.Ordinal));

        Assert.Contains("Declares no variable  (default: x)", plainRow, StringComparison.Ordinal);
        Assert.DoesNotContain("env:", plainRow, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bundle property binds through <c>CliOptionsPropertyInfo</c>, a separate implementation of
    /// <c>ICliOptionMemberInfo</c> from the parameter path. The two have drifted before (POR-59), so
    /// the bundle half is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void Render_The_Variable_For_A_Bundle_Property()
    {
        var help = Help();

        Assert.Contains("(env: POR_H_REGION)", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both suffixes appear, and in a stable order: what the value defaults to, then where else it
    /// can come from.
    /// </summary>
    [Fact]
    public void Render_The_Default_And_The_Variable_Together()
    {
        var help = Help();

        Assert.Contains("Target host  (default: localhost)  (env: POR_H_HOST)", help, StringComparison.Ordinal);
    }

    /// <summary>
    /// An author who already wrote the variable into their own description is not given a second
    /// copy — mirroring the existing guard on <c>(default: …)</c>.
    /// </summary>
    [Fact]
    public void Not_Duplicate_A_Variable_The_Author_Already_Described()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new SelfDescribedTool()))
            .Run("app run --help");

        var occurrences = result.StandardOut.Split("env:").Length - 1;
        Assert.Equal(1, occurrences);
    }

    public sealed class SelfDescribedTool
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--host", "Target host (env: POR_H_HOST)", EnvironmentVariable = "POR_H_HOST")]
            string host = "localhost") => 0;
    }
}

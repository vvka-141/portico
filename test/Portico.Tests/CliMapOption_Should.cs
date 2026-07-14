using System.Collections.Generic;
using Xunit;

namespace Portico;

// Map/dictionary option failure boundary + key semantics (SOL-76). These assert that malformed
// map options land on the usage-error boundary (exit 2, rich message) — consistent with scalar and
// collection options — and pin the duplicate-key / case-sensitivity / empty-key behaviors.
// ReSharper disable once InconsistentNaming
public sealed class CliMapOption_Should
{
    public sealed class DeployService
    {
        public IReadOnlyDictionary<string, int>? Ports { get; private set; }

        [CliRoute("deploy")]
        [CliCommandExample("deploy --ports[web] 8080")]
        public int Deploy([CliOption("--ports|-p")] Dictionary<string, int> ports)
        {
            Ports = ports;
            return 0;
        }
    }

    public sealed class ConfigService
    {
        public IReadOnlyDictionary<string, string>? Env { get; private set; }

        [CliRoute("config")]
        [CliCommandExample("config --env[region] eu")]
        public int Configure([CliOption("--env|-e")] Dictionary<string, string> env)
        {
            Env = env;
            return 0;
        }
    }

    private static (int exit, StringCliConsole console, T svc) Run<T>(T svc, string commandLine)
        where T : class
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg.WithConsole(console).AddCommands(svc));
        return (app.Run(commandLine), console, svc);
    }

    // --- Malformed value → usage-error boundary (exit 2, rich message) -----------------------

    [Fact]
    public void Map_Malformed_Value_Is_A_Usage_Error_Not_An_Unhandled_Crash()
    {
        var (exit, console, _) = Run(new DeployService(), "app.exe deploy --ports[web] notanumber");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit); // 2, not 1
        var err = console.ErrorWriter.ToString();
        Assert.Contains("notanumber", err);
        Assert.Contains("web", err);          // names the offending key
        Assert.DoesNotContain("Unhandled error", err);
    }

    [Fact]
    public void Map_Well_Formed_Values_Materialize_Into_The_Dictionary()
    {
        var (exit, _, svc) = Run(new DeployService(), "app.exe deploy --ports[web] 8080 --ports[api] 9090");

        Assert.Equal(0, exit);
        Assert.NotNull(svc.Ports);
        Assert.Equal(8080, svc.Ports!["web"]);
        Assert.Equal(9090, svc.Ports!["api"]);
    }

    // --- Duplicate key → defined usage error (not last-wins) ---------------------------------

    [Fact]
    public void Map_Duplicate_Key_Is_A_Usage_Error()
    {
        var (exit, console, _) = Run(new ConfigService(), "app.exe config --env[region] a --env[region] b");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit);
        Assert.Contains("Duplicate key 'region'", console.ErrorWriter.ToString());
    }

    // --- Key case-sensitivity: Ordinal by default (distinct keys, no collision) ---------------

    [Fact]
    public void Map_Keys_Are_Case_Sensitive_By_Default()
    {
        var (exit, _, svc) = Run(new ConfigService(), "app.exe config --env[Region] a --env[region] b");

        Assert.Equal(0, exit);
        Assert.NotNull(svc.Env);
        Assert.Equal(2, svc.Env!.Count);
        Assert.Equal("a", svc.Env!["Region"]);
        Assert.Equal("b", svc.Env!["region"]);
    }

    // --- Empty-key form → targeted message ---------------------------------------------------

    [Fact]
    public void Map_Empty_Key_With_Value_Emits_A_Clear_Message()
    {
        var (exit, console, _) = Run(new ConfigService(), "app.exe config --env[] eu");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit);
        Assert.Contains("empty map key", console.ErrorWriter.ToString());
    }

    [Fact]
    public void Map_Empty_Key_Without_Value_Emits_A_Clear_Message()
    {
        var (exit, console, _) = Run(new ConfigService(), "app.exe config --env[]");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit);
        Assert.Contains("empty map key", console.ErrorWriter.ToString());
    }
}

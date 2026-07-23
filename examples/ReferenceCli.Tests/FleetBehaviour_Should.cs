using System;
using System.Collections.Generic;
using Portico;
using Portico.Testing;
using Xunit;

namespace ReferenceCli.Tests;

/// <summary>
/// The behaviours a contract example cannot express on its own: an invalid option combination
/// rejected before the handler, a secret redacted from an audit line, an environment fallback, and
/// the injected middleware actually firing. These run the real wiring from <c>Program</c> through
/// <see cref="CliTestHarness"/>.
/// </summary>
public sealed class FleetBehaviour_Should
{
    private sealed class CapturingSink : IAuditSink
    {
        public List<string> Lines { get; } = [];
        public void Record(string line) => Lines.Add(line);
    }

    // The master CLI as Program wires it, with the injected services captured for assertions.
    private static CliTestHarness Fleet(IAuditSink sink, IClock? clock = null) =>
        CliTestHarness.ForApplication(cfg => cfg
            .AddCommands(() => new FleetTool(clock ?? new FixedClock()))
            .AddCommands(new DiagnosticsTool(), [new CliRouteAttribute("diag")])
            .UseMiddleware(new AuditMiddleware(sink))
            .WithVersion("fleet 1.0.0"));

    [Fact]
    public void Reject_An_Invalid_Bundle_Combination_Before_The_Handler()
    {
        // --max-retries without --park-on-failure is the cross-property rule JobSpec.Validate catches.
        var result = Fleet(new CapturingSink())
            .Run("fleet job submit --queue orders --max-retries 3");

        result.ExpectExit(2);
        Assert.Contains("--max-retries has no effect without --park-on-failure", result.StandardError);
    }

    [Fact]
    public void Accept_The_Valid_Form_Of_The_Same_Combination()
    {
        Fleet(new CapturingSink())
            .Run("fleet job submit --queue orders --park-on-failure true --max-retries 3")
            .ExpectExit(0)
            .ExpectOut("park=True");
    }

    [Fact]
    public void Redact_A_Sensitive_Option_From_The_Audit_Line()
    {
        var sink = new CapturingSink();

        Fleet(sink)
            .Run("fleet job submit --queue orders --auth-token hunter2 --audit")
            .ExpectExit(0);

        var audit = string.Join("\n", sink.Lines);
        Assert.Contains("***", audit);              // the value is masked...
        Assert.DoesNotContain("hunter2", audit);    // ...and the real secret never appears
    }

    [Fact]
    public void Fall_Back_To_An_Environment_Variable_For_The_Endpoint()
    {
        Environment.SetEnvironmentVariable("FLEET_ENDPOINT", "https://from-env:9000");
        try
        {
            Fleet(new CapturingSink())
                .Run("fleet cluster ping")
                .ExpectExit(0)
                .ExpectOut("pinging https://from-env:9000");

            // The command line still wins over the environment.
            Fleet(new CapturingSink())
                .Run("fleet cluster ping --endpoint https://explicit:1")
                .ExpectExit(0)
                .ExpectOut("pinging https://explicit:1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLEET_ENDPOINT", null);
        }
    }

    [Fact]
    public void Run_The_Injected_Middleware_Only_When_Enabled()
    {
        var enabled = new CapturingSink();
        Fleet(enabled).Run("fleet diag health --audit").ExpectExit(0);
        Assert.NotEmpty(enabled.Lines); // the injected sink was written through

        var disabled = new CapturingSink();
        Fleet(disabled).Run("fleet diag health").ExpectExit(0);
        Assert.Empty(disabled.Lines);   // no --audit, no records
    }

    [Fact]
    public void Treat_A_Bare_Two_State_Bool_As_An_Error()
    {
        // `--force` is a two-state bool: it needs a value. Bare `--force` is a usage error — the
        // distinction from a presence-only CliFlag? the corpus exists to teach.
        Fleet(new CapturingSink())
            .Run("fleet worker w-1 drain --force")
            .ExpectExit(2);
    }

    [Fact]
    public void Dispatch_The_Mounted_Diagnostics_Contract()
    {
        Fleet(new CapturingSink()).Run("fleet diag health").ExpectExit(0).ExpectOut("healthy");
        Fleet(new CapturingSink()).Run("fleet diag version").ExpectExit(0).ExpectOut("fleet 1.0.0");

        // The unmounted route does not survive the mount.
        Fleet(new CapturingSink()).Run("fleet health").ExpectExit(2);
    }
}

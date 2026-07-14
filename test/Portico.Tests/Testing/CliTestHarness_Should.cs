using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliTestHarness_Should
{
    public sealed class EchoService
    {
        [CliRoute("echo")]
        [CliArgument(nameof(message), "message to echo")]
        [CliCommandExample("echo hello")]
        public int Echo(string message)
        {
            Console.Out.WriteLine(message);
            return 0;
        }

        [CliRoute("fail")]
        [CliCommandExample("fail")]
        public int Fail() => throw new CliExitException("Intentional failure.") { ExitCode = 7 };
    }

    [Fact]
    public void Capture_Exit_Code_And_Chain_Fluently()
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new EchoService()));

        var result = harness.Run("app.exe echo hello");
        result.ExpectExit(0).ExpectNoError();
        Assert.Contains("hello", result.StandardOut);
    }

    [Fact]
    public void Expose_Raw_Outputs_For_Framework_Native_Assertions()
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new EchoService()));

        var result = harness.Run("app.exe echo world");

        // Users preferring xUnit assertions skip the Expect methods:
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("world", result.StandardOut);
        Assert.Equal(string.Empty, result.StandardError.Trim());
    }

    [Fact]
    public void Surface_User_Exit_Codes_And_Errors()
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new EchoService()));

        harness.Run("app.exe fail")
               .ExpectExit(7)
               .ExpectError("Intentional failure.");
    }

    [Fact]
    public void Isolate_Runs_With_Fresh_Captured_Console()
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new EchoService()));

        var a = harness.Run("app.exe echo first");
        var b = harness.Run("app.exe echo second");

        // Each run has its own stream — no carry-over between tests.
        Assert.Contains("first", a.StandardOut);
        Assert.DoesNotContain("second", a.StandardOut);
        Assert.Contains("second", b.StandardOut);
        Assert.DoesNotContain("first", b.StandardOut);
    }

    [Fact]
    public void Fail_Fluent_Assertion_With_Descriptive_Message()
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new EchoService()));

        var ex = Assert.Throws<CliTestAssertionException>(
            () => harness.Run("app.exe echo hi").ExpectExit(42));

        Assert.Contains("Expected exit code 42", ex.Message);
        Assert.Contains("got 0", ex.Message);
    }

    [Fact]
    public void Override_Caller_Console_With_Captured_One()
    {
        // Even if the caller's configure delegate sets its own console, the harness last-
        // write-wins — otherwise test output would disappear into the caller's sink.
        var callerConsole = new StringCliConsole();
        var harness = CliTestHarness.ForApplication(cfg => cfg
            .WithConsole(callerConsole)
            .AddCommands(new EchoService()));

        var result = harness.Run("app.exe echo routed");

        Assert.Contains("routed", result.StandardOut);
        Assert.Empty(callerConsole.OutWriter.ToString()); // caller's console never received anything
    }
}

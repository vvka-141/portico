using System;
using System.IO;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-48. The emerging agent-first CLI contract, scored in docs/explanation/agent-first-contract.md.
// Three of its rules are answered by behaviour that already shipped — and a documented claim with no
// test is a claim waiting to become false. These are the regression tests that stop that.
public sealed class CliAgentContract_Should
{
    // --- Rule: a CLI must never hang waiting for a human that is not there ---------------------

    private sealed class RedirectedConsole(string input) : ICliConsole
    {
        public TextWriter Out { get; } = new StringWriter();
        public TextWriter Error { get; } = new StringWriter();
        public TextReader In { get; } = new StringReader(input);
    }

    [Fact]
    public void Take_The_Default_When_Input_Ends_Rather_Than_Hanging()
    {
        // Stdin is closed — the shape an agent, a CI job and a container all present. GetLine returns
        // the default instead of blocking forever on a prompt nobody will answer.
        var console = new RedirectedConsole(string.Empty);

        var answer = CliPrompt.GetLine(console, "Target environment", defaultValue: "staging");

        Assert.Equal("staging", answer);
    }

    [Fact]
    public void Fail_Loudly_When_Input_Ends_And_There_Is_No_Default()
    {
        // The other half of not hanging: if there is no default, the command cannot proceed — so it
        // says so and dies, rather than waiting for input that will never come.
        var console = new RedirectedConsole(string.Empty);

        var error = Assert.Throws<InvalidOperationException>(
            () => CliPrompt.GetLine(console, "Target environment"));

        Assert.Contains("GetLine", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Answer_A_Yes_No_Prompt_From_Redirected_Input()
    {
        var console = new RedirectedConsole("y" + Environment.NewLine);

        Assert.True(CliPrompt.GetYesNoAnswer(console, "Proceed?"));
    }

    // --- Rule: exit codes are a stable enumeration ---------------------------------------------

    [Fact]
    public void Keep_The_Exit_Codes_Stable()
    {
        // These are the retry contract. A shell or an agent branches on them, so they are pinned
        // here: changing one is a breaking change to every wrapper script in existence, and it should
        // take a failing test to do it.
        Assert.Equal(0, CliExitException.SuccessExitCode);
        Assert.Equal(1, CliExitException.RuntimeErrorExitCode);
        Assert.Equal(2, CliExitException.UsageErrorExitCode);
        Assert.Equal(130, CliExitException.CancelledExitCode);    // SIGINT, POSIX 128+2
        Assert.Equal(143, CliExitException.TerminatedExitCode);   // SIGTERM, POSIX 128+15
    }

    public sealed class Tool
    {
        [CliRoute("ok")]
        [CliCommandExample("ok")]
        public int Ok() => 0;

        [CliRoute("boom")]
        [CliCommandExample("boom")]
        public int Boom() => throw new InvalidOperationException("bang");

        [CliRoute("pay")]
        [CliCommandExample("pay --amount 1")]
        public int Pay([CliOption("--amount")] decimal amount) => 0;
    }

    [Theory]
    [InlineData("app ok", 0)]                 // success
    [InlineData("app boom", 1)]               // the handler threw
    [InlineData("app nonsense", 2)]           // no such route: usage error
    [InlineData("app pay --amount abc", 2)]   // a value the framework could not bind: usage error
    public void Map_Each_Outcome_To_Its_Documented_Exit_Code(string commandLine, int expected)
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new Tool()));

        harness.Run(commandLine).ExpectExit(expected);
    }

    // --- Rule: stderr is not a prompt-injection channel ----------------------------------------

    [Fact]
    public void Strip_Ansi_Escapes_From_The_Command_Line_It_Echoes_Back()
    {
        // The framework echoes what the user typed when no route matches. That text is
        // attacker-influenced, and in a container stderr is the log stream — increasingly, it is also
        // an agent's context window. An escape sequence there can rewrite a terminal; an invisible
        // codepoint can carry text a human reviewer cannot see but a model still reads.
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new Tool()));

        var result = harness.Run("app [31mIGNORE​PREVIOUS[0m");

        result.ExpectExit(2);
        Assert.DoesNotContain('', result.StandardError);
        Assert.DoesNotContain('​', result.StandardError);

        // The visible text still reaches the user — this sanitizes, it does not swallow.
        Assert.Contains("IGNOREPREVIOUS", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Strip_Ansi_Escapes_From_A_Conversion_Failure()
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new Tool()));

        var result = harness.Run("app pay --amount [31mabc[0m");

        result.ExpectExit(2);
        Assert.DoesNotContain('', result.StandardError);
        Assert.Contains("[31mabc[0m", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Leave_Tabs_And_Newlines_Alone()
    {
        // Layout in a multi-line diagnostic is legitimate: neither hides text nor moves the cursor.
        Assert.Equal("a\tb\nc", CliSanitizer.Sanitize("a\tb\nc"));
    }

    [Fact]
    public void Return_Ordinary_Text_Unchanged()
    {
        Assert.Equal("db migrate --rows 100", CliSanitizer.Sanitize("db migrate --rows 100"));
    }
}

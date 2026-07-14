using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Verifies the harness's ability to feed standard input — the path used by interactive
// handlers (CliPrompt.GetYesNoAnswer, Console.ReadLine, etc.). Before this fix the harness
// redirected Console.Out/Error but not Console.In, so prompt-using handlers were untestable.
public sealed class CliTestHarnessInput_Should
{
    public sealed class ConfirmingService
    {
        [CliRoute("delete {target}")]
        [CliCommandExample("delete foo.txt")]
        public int Delete(string target)
        {
            if (CliPrompt.GetYesNoAnswer($"Delete {target}?"))
            {
                Console.Out.WriteLine($"Deleted {target}.");
                return 0;
            }
            Console.Out.WriteLine("Cancelled.");
            return 1;
        }
    }

    private static CliTestHarness BuildHarness() =>
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(new ConfirmingService()));

    [Fact]
    public void Feed_Yes_Answer_To_Prompt()
    {
        var result = BuildHarness().Run("app delete foo.txt", input: "y\n");

        result.ExpectExit(0).ExpectOut("Deleted foo.txt.");
    }

    [Fact]
    public void Feed_No_Answer_To_Prompt()
    {
        var result = BuildHarness().Run("app delete foo.txt", input: "n\n");

        result.ExpectExit(1).ExpectOut("Cancelled.");
    }

    [Fact]
    public void Feed_Invalid_Then_Valid_Answer()
    {
        // First line ('maybe') is rejected; loop re-prompts; second line ('Y') resolves.
        var result = BuildHarness().Run("app delete foo.txt", input: "maybe\nY\n");

        result.ExpectExit(0).ExpectOut("Deleted foo.txt.");
        Assert.Contains("Invalid input", result.StandardOut);
    }

    public sealed class EchoInputService
    {
        // Reads via the ICliConsole overload of CliPrompt — proves the harness-injected input
        // is visible through BOTH Console.In and ICliConsole.In.
        [CliRoute("ask")]
        [CliCommandExample("ask")]
        public int Ask()
        {
            var yes = CliPrompt.GetYesNoAnswer(SystemCliConsole.Instance, "Continue?");
            Console.Out.WriteLine(yes ? "yes" : "no");
            return 0;
        }
    }

    [Fact]
    public void Make_Fed_Input_Visible_To_ICliConsole_Path_Too()
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new EchoInputService()));

        var result = harness.Run("app ask", input: "Y\n");

        result.ExpectExit(0).ExpectOut("yes");
    }
}

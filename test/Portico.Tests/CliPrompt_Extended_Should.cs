using System;
using System.IO;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Covers the three Session 33 additions: GetLine (with default), GetPassword (masked-input
// contract; redirected-stream path), ConfirmByTyping (exact-word destructive-op guard).
// GetYesNoAnswer already has its own file.
public sealed class CliPrompt_Extended_Should
{
    private sealed class FedConsole : ICliConsole
    {
        public FedConsole(string input)
        {
            OutWriter = new StringWriter();
            ErrorWriter = new StringWriter();
            Reader = new StringReader(input);
        }

        public StringWriter OutWriter { get; }
        public StringWriter ErrorWriter { get; }
        public StringReader Reader { get; }

        public TextWriter Out => OutWriter;
        public TextWriter Error => ErrorWriter;
        public TextReader In => Reader;

        // Signals the password path that input is redirected — exercises the non-TTY branch.
        public bool IsOutputRedirected => true;
    }

    // --- GetLine ----------------------------------------------------------------------------

    [Fact]
    public void GetLine_Return_User_Input_Verbatim()
    {
        var c = new FedConsole("blue\n");
        var result = CliPrompt.GetLine(c, "Favourite colour");
        Assert.Equal("blue", result);
        Assert.Contains("Favourite colour", c.OutWriter.ToString());
    }

    [Fact]
    public void GetLine_Fall_Back_To_Default_When_User_Hits_Enter()
    {
        var c = new FedConsole("\n");
        var result = CliPrompt.GetLine(c, "Environment", defaultValue: "production");
        Assert.Equal("production", result);
        Assert.Contains("[production]", c.OutWriter.ToString());
    }

    [Fact]
    public void GetLine_Fall_Back_To_Default_When_Input_Is_Empty_Stream()
    {
        var c = new FedConsole(string.Empty);
        var result = CliPrompt.GetLine(c, "Environment", defaultValue: "staging");
        Assert.Equal("staging", result);
    }

    [Fact]
    public void GetLine_Throw_When_No_Default_And_Input_Exhausted()
    {
        var c = new FedConsole(string.Empty);
        Assert.Throws<InvalidOperationException>(() => CliPrompt.GetLine(c, "Name"));
    }

    // --- GetPassword (redirected-stream path) ------------------------------------------------

    [Fact]
    public void GetPassword_Read_From_Redirected_Stream_Without_Echo()
    {
        var c = new FedConsole("hunter2\n");
        var result = CliPrompt.GetPassword(c, "Password");
        Assert.Equal("hunter2", result);
        // The prompt text gets written, but the password itself never echoes to stdout.
        var stdout = c.OutWriter.ToString();
        Assert.Contains("Password:", stdout);
        Assert.DoesNotContain("hunter2", stdout);
    }

    [Fact]
    public void GetPassword_Throw_On_Empty_Redirected_Stream()
    {
        var c = new FedConsole(string.Empty);
        Assert.Throws<InvalidOperationException>(() => CliPrompt.GetPassword(c, "Password"));
    }

    // --- ConfirmByTyping ---------------------------------------------------------------------

    [Fact]
    public void ConfirmByTyping_Succeed_On_Exact_Match()
    {
        var c = new FedConsole("DELETE\n");
        Assert.True(CliPrompt.ConfirmByTyping(c, "This drops the database.", expected: "DELETE"));
    }

    [Fact]
    public void ConfirmByTyping_Fail_On_Case_Mismatch()
    {
        // Case-sensitive by design — a 'delete' vs 'DELETE' typo shouldn't destroy prod.
        var c = new FedConsole("delete\n");
        Assert.False(CliPrompt.ConfirmByTyping(c, "This drops the database.", expected: "DELETE"));
    }

    [Fact]
    public void ConfirmByTyping_Fail_On_Whitespace_Mismatch()
    {
        // Trims surrounding whitespace, but content must match exactly.
        var c = new FedConsole("  DELETED  \n");
        Assert.False(CliPrompt.ConfirmByTyping(c, "…", expected: "DELETE"));
    }

    [Fact]
    public void ConfirmByTyping_Fail_On_Empty_Stream()
    {
        var c = new FedConsole(string.Empty);
        Assert.False(CliPrompt.ConfirmByTyping(c, "…", expected: "DELETE"));
    }

    // --- End-to-end through the harness ------------------------------------------------------

    public sealed class DbService
    {
        public bool Dropped { get; private set; }

        [CliRoute("db drop")]
        [CliCommandExample("db drop")]
        public int Drop()
        {
            if (CliPrompt.ConfirmByTyping("This will permanently drop the production database.", "DELETE"))
            {
                Dropped = true;
                Console.Out.WriteLine("Dropped.");
                return 0;
            }
            Console.Out.WriteLine("Cancelled.");
            return 1;
        }
    }

    [Fact]
    public void Integrate_ConfirmByTyping_With_Harness_And_Fed_Input()
    {
        var svc = new DbService();
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc));

        harness.Run("app db drop", input: "DELETE\n")
               .ExpectExit(0)
               .ExpectOut("Dropped.");

        Assert.True(svc.Dropped);
    }
}

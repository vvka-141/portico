using System;
using System.IO;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliPrompt_Should
{
    private static ICliConsole ConsoleFor(string input)
    {
        var stub = new StubConsole();
        stub.Input.Append(input);
        return stub;
    }

    [Fact]
    public void Return_True_For_Capital_Y()
    {
        Assert.True(CliPrompt.GetYesNoAnswer(ConsoleFor("Y\n"), "Continue?"));
    }

    [Fact]
    public void Return_True_For_Lowercase_y()
    {
        Assert.True(CliPrompt.GetYesNoAnswer(ConsoleFor("y\n"), "Continue?"));
    }

    [Fact]
    public void Return_False_For_N_Variants()
    {
        Assert.False(CliPrompt.GetYesNoAnswer(ConsoleFor("N\n"), "Continue?"));
        Assert.False(CliPrompt.GetYesNoAnswer(ConsoleFor("n\n"), "Continue?"));
    }

    [Fact]
    public void Strip_Leading_And_Trailing_Whitespace()
    {
        Assert.True(CliPrompt.GetYesNoAnswer(ConsoleFor("  yes please  \n"), "Continue?"));
    }

    [Fact]
    public void Reprompt_On_Empty_Line_Then_Accept()
    {
        // Empty line is ignored; the next non-empty line is parsed.
        Assert.True(CliPrompt.GetYesNoAnswer(ConsoleFor("\n\nY\n"), "Continue?"));
    }

    [Fact]
    public void Reprompt_On_Invalid_Input_Then_Accept()
    {
        // Any non-Y/non-N first character triggers a retry; the next legal answer wins.
        Assert.False(CliPrompt.GetYesNoAnswer(ConsoleFor("maybe\nN\n"), "Continue?"));
    }

    [Fact]
    public void Throw_When_Input_Stream_Closes_Before_An_Answer()
    {
        // Empty input means ReadLine() returns null on first call.
        var ex = Assert.Throws<InvalidOperationException>(
            () => CliPrompt.GetYesNoAnswer(ConsoleFor(""), "Continue?"));
        Assert.Contains("end-of-stream", ex.Message);
    }

    [Fact]
    public void Echo_Prompt_To_Out()
    {
        var stub = new StubConsole();
        stub.Input.Append("Y\n");
        CliPrompt.GetYesNoAnswer(stub, "Are you sure?");
        Assert.Contains("Are you sure?", stub.OutWriter.ToString());
        Assert.Contains("(Y/N)", stub.OutWriter.ToString());
    }

    private sealed class StubConsole : ICliConsole
    {
        public StringWriter OutWriter { get; } = new();
        public StringWriter ErrorWriter { get; } = new();
        public System.Text.StringBuilder Input { get; } = new();

        public TextWriter Out => OutWriter;
        public TextWriter Error => ErrorWriter;

        // Reader is lazy-built on first In access (so the test can append to Input first), then
        // reused — otherwise the retry loop in CliPrompt would re-read the first line forever.
        private TextReader? _in;
        public TextReader In => _in ??= new StringReader(Input.ToString());
    }
}

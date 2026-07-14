using System.Collections.Generic;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-56. `--opt=value` — the GNU long-option form that git, docker, curl and dotnet all take — did
// not bind AT ALL. Not the quoting: the `=` itself. A user typed a form every CLI they have ever used
// accepts, and got exit 2 and "unknown option" for an option that was plainly in --help.
//
// The cause was that the assignment split lived in the STRING tokenizer, which argv never goes
// through. So it worked in Run(string) and failed for every real shell invocation. It now happens in
// the constructor, which every path reaches.
//
// This suite pinned the refusal; it now pins the binding.
public sealed class CliGluedOptionValue_Should
{
    public sealed class Tool
    {
        public Dictionary<string, string>? Map;
        public string? Scalar;
        public List<string>? Items;
        public string? Positional;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--cfg")] Dictionary<string, string>? cfg = null,
            [CliOption("--name|-n")] string? name = null,
            [CliOption("--tag")] List<string>? tags = null)
        {
            Map = cfg;
            Scalar = name;
            Items = tags;
            return 0;
        }

        [CliRoute("echo {value}")]
        [CliCommandExample("echo hello")]
        public int Echo(string value)
        {
            Positional = value;
            return 0;
        }
    }

    private static (int ExitCode, Tool Tool) RunArgv(params string[] args)
    {
        var tool = new Tool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        return (app.Run(args), tool);
    }

    private static (int ExitCode, Tool Tool) RunString(string commandLine)
    {
        var tool = new Tool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        return (app.Run(commandLine), tool);
    }

    // --- The space form still works (no regression) ---------------------------------------------

    [Fact]
    public void Bind_A_Space_Separated_Option()
    {
        var (exitCode, tool) = RunArgv("run", "--name", "simple");

        Assert.Equal(0, exitCode);
        Assert.Equal("simple", tool.Scalar);
    }

    [Fact]
    public void Bind_A_Space_Separated_Map_Value()
    {
        var (exitCode, tool) = RunArgv("run", "--cfg[env]", "two words");

        Assert.Equal(0, exitCode);
        Assert.Equal("two words", tool.Map!["env"]);
    }

    // --- The glued form, from argv: what a real shell hands the process -------------------------

    [Fact]
    public void Bind_A_Glued_Scalar_Option()
    {
        var (exitCode, tool) = RunArgv("run", "--name=simple");

        Assert.Equal(0, exitCode);
        Assert.Equal("simple", tool.Scalar);
    }

    [Fact]
    public void Bind_A_Glued_Scalar_Whose_Value_Contains_Spaces()
    {
        // The shell already stripped the quotes from --name="two words" and handed us one token.
        var (exitCode, tool) = RunArgv("run", "--name=two words");

        Assert.Equal(0, exitCode);
        Assert.Equal("two words", tool.Scalar);
    }

    [Fact]
    public void Bind_A_Glued_Short_Option_From_Argv_Too()
    {
        // `-f=bar` → `bar` has been the framework's reading since the port
        // (CliInvocation_FromArgs_Should.Split_Option_Assignment_Syntax pins it) — the modern-parser
        // convention rather than strict getopt, where the value would be "=bar". That decision is
        // settled; what was broken is that it only held on the STRING path. Now argv agrees.
        var (exitCode, tool) = RunArgv("run", "-n=simple");

        Assert.Equal(0, exitCode);
        Assert.Equal("simple", tool.Scalar);

        // POSIX short gluing is untouched and still binds.
        var (glued, gluedTool) = RunArgv("run", "-nsimple");

        Assert.Equal(0, glued);
        Assert.Equal("simple", gluedTool.Scalar);
    }

    [Fact]
    public void Keep_Everything_After_The_First_Separator_Verbatim()
    {
        // `docker --filter=name=foo`. Splitting on every '=' would eat the value.
        var (exitCode, tool) = RunArgv("run", "--name=name=foo");

        Assert.Equal(0, exitCode);
        Assert.Equal("name=foo", tool.Scalar);
    }

    [Fact]
    public void Bind_A_Glued_Map_Value()
    {
        var (exitCode, tool) = RunArgv("run", "--cfg[env]=prod");

        Assert.Equal(0, exitCode);
        Assert.Equal("prod", tool.Map!["env"]);
    }

    [Fact]
    public void Bind_A_Glued_Map_Value_With_Spaces()
    {
        var (exitCode, tool) = RunArgv("run", "--cfg[env]=two words");

        Assert.Equal(0, exitCode);
        Assert.Equal("two words", tool.Map!["env"]);
    }

    // --- The glued form, through Portico's own string tokenizer ---------------------------------

    [Fact]
    public void Bind_A_Glued_Quoted_Scalar_From_A_Command_Line_String()
    {
        // The tokenizer used to tear this in half at the space, because a quote that began
        // mid-token was not seen as a quote at all.
        var (exitCode, tool) = RunString("myapp run --name=\"two words\"");

        Assert.Equal(0, exitCode);
        Assert.Equal("two words", tool.Scalar);
    }

    [Fact]
    public void Bind_A_Glued_Quoted_Map_Value_From_A_Command_Line_String()
    {
        var (exitCode, tool) = RunString("myapp run --cfg[env]=\"two words\"");

        Assert.Equal(0, exitCode);
        Assert.Equal("two words", tool.Map!["env"]);
    }

    // --- The POSIX terminator still wins --------------------------------------------------------

    [Fact]
    public void Leave_A_Glued_Token_After_The_Terminator_Alone()
    {
        // After `--`, `--name=x` is a positional that merely looks like an option. Rewriting it would
        // corrupt the one thing the terminator exists to protect.
        var (exitCode, tool) = RunArgv("echo", "--", "--name=x");

        Assert.Equal(0, exitCode);
        Assert.Equal("--name=x", tool.Positional);
        Assert.Null(tool.Scalar);
    }
}

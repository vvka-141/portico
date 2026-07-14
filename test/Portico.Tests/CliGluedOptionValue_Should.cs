using System.Collections.Generic;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-56. POR-3's epic body claimed: "quoted value glued to `=`/`:` not reassembled
// (`--config[env]="two words"` tokenizes on the space)". I declined to file that without reproducing
// it — and the reproduction found something LARGER, and different:
//
//   GLUED `=` IS NOT SUPPORTED AT ALL. Not the quoting — the `=` itself. `--name=simple` exits 2,
//   and so does `--cfg[env]=value`. Portico takes `--name value` and `--cfg[env] value` (space-
//   separated), and nothing else.
//
// `--opt=value` is the GNU long-option form that git, docker, curl and dotnet all accept, and the
// Charter's own 1.0 "Conformant" axis says POSIX behaviour must match what users expect from those
// tools. A user who types it gets exit 2 and "unknown option".
//
// These tests pin TODAY'S behaviour so a fix has a baseline. When POR-56 lands they go red and are
// rewritten to assert binding — exactly as CliEnvironmentFallback_Should was under POR-54.
public sealed class CliGluedOptionValue_Should
{
    public sealed class Tool
    {
        public Dictionary<string, string>? Map;
        public string? Scalar;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--cfg")] Dictionary<string, string>? cfg = null,
            [CliOption("--name")] string? name = null)
        {
            Map = cfg;
            Scalar = name;
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

    // --- What works today ----------------------------------------------------------------------

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
        // The supported map form: `--cfg[env] value`. This is what the docs and the worked examples
        // use, and it binds a spaced value correctly.
        var (exitCode, tool) = RunArgv("run", "--cfg[env]", "two words");

        Assert.Equal(0, exitCode);
        Assert.Equal("two words", tool.Map!["env"]);
    }

    // --- The gap, pinned (POR-56) ---------------------------------------------------------------

    [Theory]
    [InlineData("--name=simple")]      // the plain GNU long form: git, docker, curl and dotnet all take it
    [InlineData("--name=two words")]   // what a real shell hands the process for --name="two words"
    public void Refuse_A_Glued_Scalar_Option_Today(string argument)
    {
        // exit 2 = usage error: the option is not recognised, so its value never reaches the handler.
        var (exitCode, tool) = RunArgv("run", argument);

        Assert.Equal(CliExitException.UsageErrorExitCode, exitCode);
        Assert.Null(tool.Scalar);
    }

    [Fact]
    public void Refuse_A_Glued_Quoted_Scalar_In_A_Command_Line_String_Today()
    {
        var (exitCode, _) = RunString("myapp run --name=\"two words\"");

        Assert.Equal(CliExitException.UsageErrorExitCode, exitCode);
    }

    [Theory]
    [InlineData("--cfg[env]=prod")]        // glued map value, from argv
    [InlineData("--cfg[env]=two words")]
    public void Refuse_A_Glued_Map_Value_Today(string argument)
    {
        var (exitCode, tool) = RunArgv("run", argument);

        Assert.Equal(CliExitException.UsageErrorExitCode, exitCode);
        Assert.Null(tool.Map);
    }

    [Fact]
    public void Refuse_A_Glued_Quoted_Map_Value_In_A_Command_Line_String_Today()
    {
        var (exitCode, _) = RunString("myapp run --cfg[env]=\"two words\"");

        Assert.Equal(CliExitException.UsageErrorExitCode, exitCode);
    }
}

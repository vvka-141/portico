using Xunit;

namespace Portico;

// Positional-after-options behavior. Portico keeps the explicit `--` terminator for 1.0
// (see CommandLine/ROADMAP.md for the decision + rationale) rather than implicit resolution. These
// pin the interim UX: an option that swallows a trailing positional now emits a targeted error that
// names the token and points at `--`, and the `--` workaround is verified end-to-end.
// ReSharper disable once InconsistentNaming
public sealed class CliPositionalAfterOption_Should
{
    public sealed class FlagService
    {
        [CliRoute("check")]
        [CliCommandExample("check -v")]
        public int Check([CliOption("--verbose|-v")] CliFlag? verbose = null) => 0;
    }

    public sealed class BuildService
    {
        [CliRoute("build")]
        [CliCommandExample("build --output out.dll")]
        public int Build([CliOption("--output|-o")] string output) => 0;
    }

    public sealed class CompileService
    {
        public string? Source { get; private set; }
        public string? Output { get; private set; }

        [CliRoute("compile {source}")]
        [CliCommandExample("compile main.cs")]
        public int Compile(string source, [CliOption("--output|-o")] string output = "a.out")
        {
            Source = source;
            Output = output;
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

    // --- Interim targeted error (implicit form is not supported) ------------------------------

    [Fact]
    public void Flag_That_Swallows_A_Positional_Names_The_Token_And_Points_At_Terminator()
    {
        var (exit, console, _) = Run(new FlagService(), "app.exe check -v file.txt");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit);
        var err = console.ErrorWriter.ToString();
        Assert.Contains("file.txt", err);
        Assert.Contains("--", err);                    // reminds the user about the terminator
        Assert.DoesNotContain("Unhandled error", err);
    }

    [Fact]
    public void Scalar_That_Swallows_Extra_Tokens_Names_Them_And_Points_At_Terminator()
    {
        var (exit, console, _) = Run(new BuildService(), "app.exe build --output out.dll main.cs");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit);
        var err = console.ErrorWriter.ToString();
        Assert.Contains("out.dll", err);
        Assert.Contains("main.cs", err);
        Assert.Contains("--", err);
    }

    // --- The documented `--` terminator resolves positional-after-option ----------------------

    [Fact]
    public void Terminator_Resolves_A_Positional_That_Follows_An_Option()
    {
        var (exit, _, svc) = Run(new CompileService(), "app.exe compile --output out.dll -- main.cs");

        Assert.Equal(0, exit);
        Assert.Equal("main.cs", svc.Source);
        Assert.Equal("out.dll", svc.Output);
    }

    [Fact]
    public void Natural_Order_Positional_Before_Option_Works()
    {
        var (exit, _, svc) = Run(new CompileService(), "app.exe compile main.cs --output out.dll");

        Assert.Equal(0, exit);
        Assert.Equal("main.cs", svc.Source);
        Assert.Equal("out.dll", svc.Output);
    }
}

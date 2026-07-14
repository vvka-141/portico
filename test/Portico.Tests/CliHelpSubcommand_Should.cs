using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// `app help <command>` — the git convention — shows that command's help (SOL-84 m1). A bare
// `app help` still falls through to the general command listing.
public sealed class CliHelpSubcommand_Should
{
    public sealed class GitLikeService
    {
        [CliRoute("init")]
        [CliCommandExample("init")]
        public int Init() => 0;

        [CliRoute("deploy {target}")]
        [CliCommandExample("deploy prod")]
        public int Deploy(string target) => 0;
    }

    [Fact]
    public void Show_Command_Help_For_A_Leading_Help_Subcommand()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new GitLikeService()));

        Assert.Equal(0, app.Run("app.exe help init"));

        var output = console.OutWriter.ToString();
        Assert.Contains("init", output);
        // Command-specific help, not the general listing (which would also mention deploy).
        Assert.DoesNotContain("deploy", output);
    }

    [Fact]
    public void Show_General_Help_For_A_Bare_Help()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new GitLikeService()));

        Assert.Equal(0, app.Run("app.exe help"));

        var output = console.OutWriter.ToString();
        Assert.Contains("init", output);
        Assert.Contains("deploy", output);
    }

    [Fact]
    public void Still_Support_The_Trailing_Help_Flag()
    {
        // The pre-existing `app <command> --help` form is unaffected.
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new GitLikeService()));

        Assert.Equal(0, app.Run("app.exe init --help"));

        var output = console.OutWriter.ToString();
        Assert.Contains("init", output);
        Assert.DoesNotContain("deploy", output);
    }

    [Fact]
    public void Separate_Unrecognized_Options_With_Comma_Space()
    {
        // SOL-84 n1: the unrecognized-option list joins with ", " to match the suggestion list.
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new GitLikeService()));

        var exit = app.Run("app.exe init --aa --bb");

        Assert.NotEqual(0, exit);
        Assert.Contains("--aa, --bb", console.ErrorWriter.ToString());
    }
}

using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Snapshot-style tests that pin the EXACT help output, character by character. Any format
// regression (an extra space, a missing newline, a different alias separator) trips these
// tests with a visible diff. This is the testability T4 never actually gave us —
// 'Assert.Contains("PATH", help)' misses layout shifts entirely.
//
// If you're intentionally changing help layout, update the expected string here and in
// any affected documentation. Snapshot tests don't prevent changes; they make them
// visible and deliberate.
public sealed class CliHelp_Layout_Should
{
    public interface IDeployContract
    {
        [CliRoute("deploy {env}")]
        [CliCommandExample("deploy prod", "Deploy to production")]
        [CliCommandExample("deploy staging --dry-run", "Preview without applying")]
        [System.ComponentModel.Description("Deploy the application to a target environment.")]
        int Deploy(
            string env,
            [CliOption("--dry-run|-n", "Preview without applying changes")] CliFlag? dryRun = null,
            [CliOption("--replicas|-r", "Number of replicas", DefaultValue = "3")] int replicas = 3);
    }

    public sealed class DeployService : IDeployContract
    {
        public int Deploy(string env, CliFlag? dryRun, int replicas) => 0;
    }

    // --- Per-command help snapshot ----------------------------------------------------------

    [Fact]
    public void Pin_PerCommand_Help_Layout()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new DeployService())
            .WithConsole(console));

        app.Run("myapp deploy --help");

        // Normalise line endings so the assertion works on Windows + Unix.
        var actual = console.OutWriter.ToString().Replace("\r\n", "\n");
        const string expected =
            "Usage: myapp deploy <ENV> [options]\n" +
            "\n" +
            "Deploy the application to a target environment.\n" +
            "\n" +
            "Arguments:\n" +
            "  ENV               env\n" +
            "\n" +
            "Options:\n" +
            "  --dry-run, -n       Preview without applying changes\n" +
            "  --replicas, -r      Number of replicas  (default: 3)\n" +
            "\n" +
            "Examples:\n" +
            "  myapp deploy prod\n" +
            "      Deploy to production\n" +
            "  myapp deploy staging --dry-run\n" +
            "      Preview without applying\n" +
            "\n";

        Assert.Equal(expected, actual);
    }

    // --- General help snapshot --------------------------------------------------------------

    [Fact]
    public void Pin_GeneralHelp_Layout_For_A_Single_Command_Cli()
    {
        // POR-31: one route means there is no menu to show. A "Commands:" list of one, followed by
        // "run 'myapp <command> --help' for more information", would make the user type a second
        // command to learn what the tool they just invoked does. So a single-command CLI gets that
        // command's full help — the shape `grep --help` has.
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new DeployService())
            .WithConsole(console));

        app.Run("myapp --help");

        var actual = console.OutWriter.ToString().Replace("\r\n", "\n");
        const string expected =
            "Usage: myapp deploy <ENV> [options]\n" +
            "\n" +
            "Deploy the application to a target environment.\n" +
            "\n" +
            "Arguments:\n" +
            "  ENV               env\n" +
            "\n" +
            "Options:\n" +
            "  --dry-run, -n       Preview without applying changes\n" +
            "  --replicas, -r      Number of replicas  (default: 3)\n" +
            "\n" +
            "Examples:\n" +
            "  myapp deploy prod\n" +
            "      Deploy to production\n" +
            "  myapp deploy staging --dry-run\n" +
            "      Preview without applying\n" +
            "\n";

        Assert.Equal(expected, actual);
    }

    // --- Style consistency assertions -------------------------------------------------------

    [Fact]
    public void Use_Comma_Separated_Aliases_In_Both_Help_Paths()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new DeployService())
            .WithConsole(console));

        app.Run("myapp deploy --help");
        var perCommand = console.OutWriter.ToString();
        Assert.Contains("--dry-run, -n", perCommand);
        Assert.DoesNotContain("--dry-run|-n", perCommand);

        console.OutWriter.GetStringBuilder().Clear();
        app.Run("myapp --help");
        var general = console.OutWriter.ToString();
        Assert.Contains("--dry-run, -n", general);
        Assert.DoesNotContain("--dry-run|-n", general);
    }

    [Fact]
    public void Never_Emit_Trailing_Whitespace_In_Help_Output()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new DeployService())
            .WithConsole(console));

        app.Run("myapp --help");

        var lines = console.OutWriter.ToString().Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            Assert.False(line.Length > 0 && char.IsWhiteSpace(line[^1]),
                $"Help line has trailing whitespace: [{line}]");
        }
    }

    [Fact]
    public void Render_Default_Value_Exactly_Once()
    {
        // Regression: the framework appends "(default: X)"; if the user's description
        // already contained "default:", both used to render. Now suppressed.
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new DeployService())
            .WithConsole(console));

        app.Run("myapp deploy --help");
        var help = console.OutWriter.ToString();

        var matches = System.Text.RegularExpressions.Regex.Matches(help, @"\(default: 3\)");
        Assert.Single(matches);
    }
}

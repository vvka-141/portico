using System;
using System.ComponentModel;
using System.Linq;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-31: `myapp --help` — the first thing anyone runs — used to concatenate the FULL detail of
// every command (usage, arguments, every option) into one wall. It listed no commands and no
// descriptions. Portico already had the drill-down half of the two-level help model that git,
// docker, dotnet and kubectl all implement; this pins the summary half.
public sealed class CliTopLevelHelp_Should
{
    // The shape Portico is positioned for: a backend admin CLI. Under the old renderer this printed
    // every option of all five commands.
    public interface IAdminContract
    {
        [CliRoute("db migrate")]
        [CliCommandExample("db migrate --connection-string \"Host=db\"")]
        [Description("Apply pending database migrations.")]
        int Migrate([CliOption("--connection-string|-c", "Postgres connection string")] string connectionString);

        [CliRoute("db seed")]
        [CliCommandExample("db seed --rows 100")]
        [Description("Seed reference data.")]
        int Seed([CliOption("--rows", "How many rows")] int rows = 10);

        [CliRoute("reindex {index}")]
        [CliCommandExample("reindex orders")]
        [Description("Rebuild a search index.")]
        int Reindex(string index);

        [CliRoute("drain")]
        [CliCommandExample("drain")]
        [Description("Drain in-flight work.")]
        int Drain([CliOption("--timeout", "How long to wait")] TimeSpan? timeout = null);

        // Deliberately undescribed: the description column must stay blank rather than echo the
        // method name back at the user.
        [CliRoute("health")]
        [CliCommandExample("health")]
        int Health();
    }

    public sealed class AdminService : IAdminContract
    {
        public int Migrate(string connectionString) => 0;
        public int Seed(int rows) => 0;
        public int Reindex(string index) => 0;
        public int Drain(TimeSpan? timeout) => 0;
        public int Health() => 0;
    }

    private static string Help(string commandLine)
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new AdminService())
            .WithConsole(console));

        Assert.Equal(0, app.Run(commandLine));
        return console.OutWriter.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void Pin_The_Command_Summary_Layout()
    {
        const string expected =
            "Usage: admin <command> [options]\n" +
            "\n" +
            "Commands:\n" +
            "  db migrate          Apply pending database migrations.\n" +
            "  db seed             Seed reference data.\n" +
            "  reindex <INDEX>     Rebuild a search index.\n" +
            "  drain               Drain in-flight work.\n" +
            "  health\n" +
            "\n" +
            "Run 'admin <command> --help' for more information on a command.\n" +
            "\n";

        Assert.Equal(expected, Help("admin --help"));
    }

    [Fact]
    public void List_Every_Command_And_No_Option_Of_Any_Of_Them()
    {
        var help = Help("admin --help");

        Assert.Contains("db migrate", help);
        Assert.Contains("db seed", help);
        Assert.Contains("reindex", help);
        Assert.Contains("drain", help);
        Assert.Contains("health", help);

        // The whole bug: the top level used to print every command's option list.
        Assert.DoesNotContain("--connection-string", help);
        Assert.DoesNotContain("--rows", help);
        Assert.DoesNotContain("--timeout", help);
        Assert.DoesNotContain("Options:", help);
        Assert.DoesNotContain("Examples:", help);
    }

    [Fact]
    public void Point_The_User_At_The_Drill_Down()
    {
        Assert.Contains("Run 'admin <command> --help' for more information on a command.", Help("admin --help"));
    }

    [Fact]
    public void Emit_No_Trailing_Whitespace_On_Any_Line()
    {
        // `health` has no description, so its row must not be padded out to the column.
        foreach (var line in Help("admin --help").Split('\n'))
        {
            Assert.Equal(line.TrimEnd(), line);
        }

        foreach (var line in Help("admin db migrate --help").Split('\n'))
        {
            Assert.Equal(line.TrimEnd(), line);
        }
    }

    [Fact]
    public void Leave_Per_Command_Help_Alone()
    {
        var help = Help("admin db migrate --help");

        Assert.Contains("Usage: admin db migrate [options]", help);
        Assert.Contains("--connection-string, -c", help);
        Assert.Contains("Examples:", help);
        Assert.Contains("admin db migrate --connection-string \"Host=db\"", help);
        Assert.DoesNotContain("Commands:", help);
    }

    [Fact]
    public void Show_The_Same_Summary_For_A_Bare_Invocation()
    {
        // No args at all is the other way users arrive at the top level.
        Assert.Equal(Help("admin --help"), Help("admin"));
    }
}

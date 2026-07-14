using System;
using System.Linq;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-39: a [CliCommandExample] is authored against the contract, which cannot know the root route
// it is later mounted under. Help used to print the attribute text verbatim, so every example in a
// composed CLI was a confidently-wrong command that exits 2 when pasted.
public sealed class CliMountedExampleHelp_Should
{
    public sealed class AwsTool
    {
        [CliRoute("deploy")]
        [CliCommandExample("deploy --region eu-west-1")]
        public int Deploy([CliOption("--region")] string region) => 0;
    }

    [CliRoute("db")]
    public sealed class MigrationTool
    {
        [CliRoute("migrate")]
        [CliCommandExample("db migrate --rows 100")]
        public int Migrate([CliOption("--rows")] int rows) => 0;
    }

    private static string CommandHelp(StringCliConsole console, CliApplication app, string commandLine)
    {
        Assert.Equal(0, app.Run(commandLine));
        return console.OutWriter.ToString();
    }

    // The example line, with the "  master " prefix stripped — i.e. exactly what a user pastes.
    private static string ExampleCommand(string help, string executableName)
    {
        var line = help
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .SkipWhile(l => !l.StartsWith("Examples:", StringComparison.Ordinal))
            .Skip(1)
            .First(l => l.StartsWith("  ", StringComparison.Ordinal));

        line = line.Trim();
        Assert.StartsWith(executableName + " ", line);
        return line;
    }

    [Fact]
    public void Prefix_A_Mounted_Examples_With_Its_Root_Route()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new AwsTool(), [new CliRouteAttribute("aws")]));

        var help = CommandHelp(console, app, "master aws deploy --help");

        Assert.Contains("  master aws deploy --region eu-west-1", help);
        Assert.DoesNotContain("  master deploy --region eu-west-1", help);
    }

    [Fact]
    public void Print_An_Example_That_Actually_Dispatches()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new AwsTool(), [new CliRouteAttribute("aws")]));

        var help = CommandHelp(console, app, "master aws deploy --help");
        var pasted = ExampleCommand(help, "master");

        // The whole point: copy the line out of help, paste it back, and it runs.
        Assert.Equal(0, app.Run(pasted));
    }

    [Fact]
    public void Compose_The_Mount_On_Top_Of_A_Type_Level_Route_Prefix()
    {
        // The type-level [CliRoute("db")] is visible to the example's author (the example says
        // "db migrate ..."); only the mount is prepended.
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new MigrationTool(), [new CliRouteAttribute("ops")]));

        var help = CommandHelp(console, app, "master ops db migrate --help");
        var pasted = ExampleCommand(help, "master");

        Assert.Equal("master ops db migrate --rows 100", pasted);
        Assert.Equal(0, app.Run(pasted));
    }

    [Fact]
    public void Leave_An_Unmounted_Example_Exactly_As_It_Was()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new AwsTool()));

        var help = CommandHelp(console, app, "master deploy --help");

        Assert.Equal("master deploy --region eu-west-1", ExampleCommand(help, "master"));
    }
}

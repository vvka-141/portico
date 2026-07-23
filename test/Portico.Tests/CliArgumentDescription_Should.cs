using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// [CliArgument] on a placeholder-bound parameter is the recommended shape (POR-70): the route
// string declares the path, the attribute describes one of its segments — the CLI's [FromRoute].
// It must never change the route, only the help. Its ability to override the DISPLAY name via
// Name = "..." is pinned here too: the reflection pipeline defaults that property from the
// parameter name and must not overwrite an author's choice.
public sealed class CliArgumentDescription_Should
{
    public interface IAugmentedDescription
    {
        [CliRoute("deploy {env}")]
        [CliCommandExample("deploy prod")]
        int Deploy([CliArgument("Target environment name")] string env);
    }

    public sealed class AugmentedService : IAugmentedDescription
    {
        public int Deploy(string env) => 0;
    }

    [Fact]
    public void Accept_Parameter_Level_CliArgument_On_Placeholder_Bound_Parameter()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new AugmentedService())
            .WithConsole(console));

        // Route still dispatches — proving the parameter-level attribute didn't break binding.
        Assert.Equal(0, app.Run("app deploy prod"));
    }

    [Fact]
    public void Render_Parameter_Level_Description_In_Help()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new AugmentedService())
            .WithConsole(console));

        app.Run("app deploy --help");
        var help = console.OutWriter.ToString();

        // The description should appear in the Arguments table, not the default "env".
        Assert.Contains("Target environment name", help);
    }

    [Fact]
    public void Leave_The_Route_Signature_Untouched()
    {
        var described = CliApplication
            .Create(cfg => cfg.AddCommands(new AugmentedService()))
            .GetRouteSignatures();

        // Byte-identical to the same route with no [CliArgument] at all — the attribute describes,
        // it does not route.
        Assert.Equal(["deploy {env}"], described);
    }

    public interface IRenamedDisplay
    {
        [CliRoute("init {projectDir}")]
        [CliCommandExample("init .")]
        int Initialize([CliArgument("Where to scaffold", Name = "PROJECT_DIR")] string projectDir);
    }

    public sealed class RenamedDisplayService : IRenamedDisplay
    {
        public int Initialize(string projectDir) => 0;
    }

    [Fact]
    public void Honour_An_Explicit_Display_Name_Override()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .AddCommands(new RenamedDisplayService())
            .WithConsole(console));

        app.Run("app init --help");
        var help = console.OutWriter.ToString();

        Assert.Contains("PROJECT_DIR", help);
        Assert.DoesNotContain("<PROJECTDIR>", help);
    }
}

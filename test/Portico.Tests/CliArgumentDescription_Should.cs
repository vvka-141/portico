using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Before S35 the framework rejected parameter-level [CliArgument("description")] on a
// placeholder-bound parameter with an error message that suggested using exactly that
// attribute — a documented-footgun loop. Now it's supported and augments the synthesized
// argument's description; method-level [CliArgument(nameof(x), ...)] is still rejected as
// a genuine routing conflict.
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

    public sealed class DoubleDeclaredService
    {
        // Method-level [CliArgument(nameof(path), …)] + placeholder is still a conflict.
        [CliRoute("init {path}")]
        [CliArgument(nameof(path), "a path")]
        [CliCommandExample("init .")]
        public int Init(string path) => 0;
    }

    [Fact]
    public void Still_Reject_Method_Level_CliArgument_On_Placeholder_Parameter()
    {
        var ex = Assert.ThrowsAny<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new DoubleDeclaredService())));

        Assert.Contains("{path}", ex.Message);
        Assert.Contains("[CliArgument]", ex.Message);
        Assert.Contains("method-level", ex.Message);
    }
}

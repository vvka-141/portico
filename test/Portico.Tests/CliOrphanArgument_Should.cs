using Xunit;

namespace Portico;

// POR-63. A method-level [CliArgument("name", …)] contributes a route segment (ExtractRouteParts);
// if its name matches no parameter it used to be silently dropped, leaving the route demanding a
// positional token that binds to nothing. It must fail loudly at configuration time, exactly as the
// sibling {placeholder} path already does (ResolveRoutePlaceholders).
// ReSharper disable once InconsistentNaming
public sealed class CliOrphanArgument_Should
{
    public sealed class OrphanArgService
    {
        [CliRoute("copy")]
        [CliArgument("nope", "an argument that references no parameter")]
        [CliCommandExample("copy x")]
        public int Copy(string source) => 0;
    }

    public sealed class ValidArgService
    {
        [CliRoute("copy")]
        [CliArgument("source", "the source path")]
        [CliCommandExample("copy x")]
        public int Copy(string source) => 0;
    }

    [Fact]
    public void Reject_A_Method_Level_Argument_That_References_No_Parameter()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new OrphanArgService())));

        Assert.Contains("nope", ex.Message);   // names the orphaned argument
        Assert.Contains("Copy", ex.Message);   // and the method it is on
    }

    [Fact]
    public void Accept_A_Method_Level_Argument_That_References_A_Real_Parameter()
    {
        var app = CliApplication.Create(cfg => cfg.AddCommands(new ValidArgService()));

        Assert.Equal(0, app.Run("app.exe copy myfile"));
    }
}

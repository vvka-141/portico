using System.Collections.Generic;
using System.Linq;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-40: a contract composed into a master CLI under a root route must be validated in the
// position it ships in. The validator used to register the DispatchProxy unmounted, so it was
// green on examples the shipped, composed CLI rejects with exit 2.
public sealed class CliContractValidatorMount_Should
{
    // Two contracts that collide on the literal `deploy` — the composition case worth proving.
    public interface IAwsTool
    {
        [CliRoute("deploy")]
        [CliCommandExample("deploy --region eu-west-1")]
        int Deploy([CliOption("--region")] string region);
    }

    public interface IAzureTool
    {
        [CliRoute("deploy")]
        [CliCommandExample("deploy --resource-group rg1")]
        int Deploy([CliOption("--resource-group")] string resourceGroup);
    }

    private sealed class AwsTool : IAwsTool
    {
        public int Deploy(string region) => 0;
    }

    private sealed class AzureTool : IAzureTool
    {
        public int Deploy(string resourceGroup) => 0;
    }

    [Fact]
    public void Validate_A_Contract_In_The_Position_It_Ships_In()
    {
        var e = Assert.Single(new CliContractValidator<IAwsTool>("aws").Enumerate());

        Assert.True(e.Matched);
        Assert.Equal(nameof(IAwsTool.Deploy), e.Handler);
        Assert.Equal("eu-west-1", e.Arguments["region"]);
    }

    // An example must be authored against the contract, never against the mount — the mount is the
    // composer's choice and the validator (like help) prepends it. Spelling it out anyway yields
    // `aws aws deploy`, which routes nowhere.
    public interface IDoubleMountedTool
    {
        [CliRoute("deploy")]
        [CliCommandExample("deploy --region eu-west-1")]
        [CliCommandExample("aws deploy --region eu-west-1")]
        int Deploy([CliOption("--region")] string region);
    }

    [Fact]
    public void Fail_An_Example_That_Does_Not_Dispatch_Under_The_Mount()
    {
        var invoked = new List<string>();
        var notInvoked = new List<string>();

        new CliContractValidator<IDoubleMountedTool>("aws").Validate(
            onNotInvoked: ex => notInvoked.Add(ex.Example),
            onInvoked: ex => invoked.Add(ex.Example));

        Assert.Equal(["deploy --region eu-west-1"], invoked);
        Assert.Equal(["aws deploy --region eu-west-1"], notInvoked);
    }

    [Fact]
    public void Verify_Both_Surfaces_Of_A_Composed_Application()
    {
        // Each validator proxies its own contract and composes the *other* tool in for real, so both
        // run against the whole route table — colliding `deploy` literals and all.
        var aws = Assert.Single(new CliContractValidator<IAwsTool>("aws").Enumerate(
            configureApplication: cfg => cfg.AddCommands(new AzureTool(), [new CliRouteAttribute("azure")])));

        var azure = Assert.Single(new CliContractValidator<IAzureTool>("azure").Enumerate(
            configureApplication: cfg => cfg.AddCommands(new AwsTool(), [new CliRouteAttribute("aws")])));

        Assert.True(aws.Matched);
        Assert.Equal(nameof(IAwsTool.Deploy), aws.Handler);
        Assert.Equal("eu-west-1", aws.Arguments["region"]);

        Assert.True(azure.Matched);
        Assert.Equal(nameof(IAzureTool.Deploy), azure.Handler);
        Assert.Equal("rg1", azure.Arguments["resourceGroup"]);
    }

    [Fact]
    public void Accept_A_Multi_Segment_Root_Route()
    {
        var e = Assert.Single(new CliContractValidator<IAwsTool>("cloud aws").Enumerate());

        Assert.True(e.Matched);
        Assert.Equal(nameof(IAwsTool.Deploy), e.Handler);
    }

    [Fact]
    public void Leave_An_Unmounted_Contract_Exactly_As_It_Was()
    {
        var e = Assert.Single(new CliContractValidator<IAwsTool>().Enumerate());

        Assert.True(e.Matched);
        Assert.Equal("deploy --region eu-west-1", e.Example);
        Assert.Equal(nameof(IAwsTool.Deploy), e.Handler);
    }

    [Fact]
    public void Agree_With_The_Application_It_Claims_To_Model()
    {
        // The regression guard: whatever the validator reports must hold in a real composed app.
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(new StringCliConsole())
            .AddCommands(new AwsTool(), [new CliRouteAttribute("aws")])
            .AddCommands(new AzureTool(), [new CliRouteAttribute("azure")]));

        var examples = new CliContractValidator<IAwsTool>("aws").Enumerate();

        Assert.All(examples, e => Assert.True(e.Matched));
        Assert.All(examples, e => Assert.Equal(0, app.Run($"master aws {e.Example}")));

        // ...and the unmounted form the old validator was green on is, in fact, rejected.
        Assert.Equal(2, app.Run("master deploy --region eu-west-1"));
    }
}

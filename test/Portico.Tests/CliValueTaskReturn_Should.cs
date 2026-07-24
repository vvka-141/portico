using System.Threading.Tasks;
using Xunit;

namespace Portico;

// POR008 is an authoring aid, not the runtime's only line of defence. Consumers can suppress
// analyzers or load contracts from separately-built assemblies, so application construction must
// enforce the same handler return contract.
// ReSharper disable once InconsistentNaming
public sealed class CliValueTaskReturn_Should
{
    public sealed class ValueTaskOfIntService
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public ValueTask<int> Run() => ValueTask.FromResult(7);
    }

    public sealed class PlainValueTaskService
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public ValueTask Run() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Reject_ValueTask_Of_Int_When_Application_Is_Created()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new ValueTaskOfIntService())));

        Assert.Contains("ValueTask", ex.Message);
        Assert.Contains("int or Task<int>", ex.Message);
    }

    [Fact]
    public void Reject_Plain_ValueTask_When_Application_Is_Created()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new PlainValueTaskService())));

        Assert.Contains("ValueTask", ex.Message);
        Assert.Contains("int or Task<int>", ex.Message);
    }
}

using System.Linq;
using Portico.Testing;
using Xunit;

namespace Portico;

public sealed class CliContractValidatorHandler_Should
{
    public interface IMisattributed
    {
        [CliRoute("backup")]
        [CliCommandExample("backup")]
        int Backup();

        [CliRoute("restore")]
        [CliCommandExample("restore")]
        [CliCommandExample("backup", "wrong: declared on Restore but dispatches to Backup")]
        int Restore();
    }

    [Fact]
    public void Fail_An_Example_That_Dispatches_To_The_Wrong_Handler()
    {
        var results = new CliContractValidator<IMisattributed>().Enumerate();
        var wrongOne = results.Single(e => e.Description.Contains("wrong"));

        Assert.False(wrongOne.Matched,
            $"Expected handler-mismatch failure but got Matched=true. Handler={wrongOne.Handler}");
        Assert.NotNull(wrongOne.FailureReason);
        Assert.Contains("Backup", wrongOne.FailureReason!);
        Assert.Contains("Restore", wrongOne.FailureReason!);
    }

    [Fact]
    public void Pass_When_Example_Dispatches_To_Its_Declaring_Handler()
    {
        var results = new CliContractValidator<IMisattributed>().Enumerate();
        var correctOnes = results.Where(e => !e.Description.Contains("wrong")).ToList();

        Assert.All(correctOnes, e =>
            Assert.True(e.Matched, $"Example '{e.Example}' failed: {e.FailureReason}"));
    }

    [Fact]
    public void Fail_Wrong_Handler_Via_Validate()
    {
        int failures = 0;
        string? reason = null;

        new CliContractValidator<IMisattributed>().Validate(
            onNotInvoked: (attr, r) =>
            {
                if (attr.Description.Contains("wrong"))
                {
                    failures++;
                    reason = r;
                }
            });

        Assert.Equal(1, failures);
        Assert.NotNull(reason);
        Assert.Contains("Backup", reason!);
        Assert.Contains("Restore", reason!);
    }
}

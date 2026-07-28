using System;
using System.Linq;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-128. The validator's promise is that an example reaches the method it is declared on.
// Handler matching compared method *names*, which two overloads share — so an example declared
// on one overload could dispatch to the other and still report Matched = true.
public sealed class CliContractValidatorOverloads_Should
{
    // `db seed` matches the no-argument overload exactly. The example below is declared on the
    // one-argument overload, so it reaches the wrong Seed — the mistake a name comparison
    // cannot see.
    public interface IMisdeclared
    {
        [CliRoute("db seed")]
        [CliCommandExample("db seed")]
        int Seed();

        [CliRoute("db seed {file}")]
        [CliCommandExample("db seed")]
        int Seed(string file);
    }

    public interface IWellDeclared
    {
        [CliRoute("db seed")]
        [CliCommandExample("db seed")]
        int Seed();

        [CliRoute("db seed {file}")]
        [CliCommandExample("db seed rows.csv")]
        int Seed(string file);
    }

    [Fact]
    public void Reject_An_Example_That_Reaches_A_Different_Overload()
    {
        var results = new CliContractValidator<IMisdeclared>().Enumerate();

        var misdeclared = results.Single(r => !r.Matched);
        Assert.Equal("db seed", misdeclared.Example);
        Assert.Contains("dispatched to", misdeclared.FailureReason!, StringComparison.Ordinal);
    }

    // Without signatures the message reads "dispatched to 'Seed' instead of 'Seed'", which names
    // the defect without describing it.
    [Fact]
    public void Name_The_Parameter_Types_When_Only_The_Overload_Differs()
    {
        var reason = new CliContractValidator<IMisdeclared>()
            .Enumerate()
            .Single(r => !r.Matched)
            .FailureReason!;

        Assert.Contains("Seed()", reason, StringComparison.Ordinal);
        Assert.Contains("Seed(String)", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_Overloads_Whose_Examples_Each_Reach_Their_Own_Method()
    {
        var results = new CliContractValidator<IWellDeclared>().Enumerate();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Matched, r.FailureReason));
        Assert.All(results, r => Assert.Equal("Seed", r.Handler));
    }

    // The public descriptor still carries a bare name — the MethodInfo is an internal detail of
    // the comparison, not an API change.
    [Fact]
    public void Keep_Reporting_The_Handler_As_A_Plain_Name()
    {
        var matched = new CliContractValidator<IWellDeclared>()
            .Enumerate()
            .Single(r => r.Example == "db seed rows.csv");

        Assert.Equal(nameof(IWellDeclared.Seed), matched.Handler);
        Assert.Equal("rows.csv", matched.Arguments["file"]);
    }
}

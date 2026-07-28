using System;
using Xunit;

namespace Portico;

// POR-117. A malformed [CliOption] specification escaped CliApplication.Create as a raw
// ArgumentException instead of CliConfigurationException. Every other configuration fault
// throws CliConfigurationException; this was the outlier.
public sealed class CliMalformedSpec_Should
{
    // --- No leading dash ----------------------------------------------------------

    public interface INoDash
    {
        [CliRoute("go")]
        [CliCommandExample("go --name Alice")]
        int Go([CliOption("name")] string name);
    }

    private sealed class NoDash : INoDash
    {
        public int Go(string name) => 0;
    }

    [Fact]
    public void Surface_As_CliConfigurationException_When_No_Leading_Dash()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new NoDash())));

        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Go", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter '", ex.Message, StringComparison.Ordinal);
    }

    // --- Invalid alias name -------------------------------------------------------

    public interface IInvalidAlias
    {
        [CliRoute("run")]
        [CliCommandExample("run --ok yes")]
        int Run([CliOption("--my name")] string val);
    }

    private sealed class InvalidAlias : IInvalidAlias
    {
        public int Run(string val) => 0;
    }

    [Fact]
    public void Surface_As_CliConfigurationException_When_Invalid_Alias()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new InvalidAlias())));

        Assert.Contains("val", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Run", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter '", ex.Message, StringComparison.Ordinal);
    }

    // --- Duplicate alias within one spec ------------------------------------------

    public interface IDuplicateAlias
    {
        [CliRoute("check")]
        [CliCommandExample("check --n x")]
        int Check([CliOption("--n|--n")] string val);
    }

    private sealed class DuplicateAlias : IDuplicateAlias
    {
        public int Check(string val) => 0;
    }

    [Fact]
    public void Surface_As_CliConfigurationException_When_Duplicate_Alias()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new DuplicateAlias())));

        Assert.Contains("val", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Check", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter '", ex.Message, StringComparison.Ordinal);
    }

    // --- Message names the declaring method and parameter -------------------------

    [Fact]
    public void Name_The_Declaring_Method_And_Parameter()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new NoDash())));

        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Go", ex.Message, StringComparison.Ordinal);
    }

    // --- POR-118: case-variant self-collision catches at Parse, translated by POR-117 ---

    public interface ICaseVariant
    {
        [CliRoute("go")]
        [CliCommandExample("go --name Alice")]
        int Go([CliOption("--name|--NAME")] string name);
    }

    private sealed class CaseVariant : ICaseVariant
    {
        public int Go(string name) => 0;
    }

    [Fact]
    public void Surface_Case_Variant_Self_Collision_As_CliConfigurationException()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new CaseVariant())));

        Assert.Contains("differ only by case", ex.Message, StringComparison.Ordinal);
        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(Parameter '", ex.Message, StringComparison.Ordinal);
    }
}

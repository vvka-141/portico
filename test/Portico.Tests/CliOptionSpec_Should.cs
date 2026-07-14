using System;
using Xunit;

namespace Portico;

// Spec-string parsing exercised in isolation, without constructing a CliOptionAttribute (SOL-79).
// ReSharper disable once InconsistentNaming
public sealed class CliOptionSpec_Should
{
    [Fact]
    public void Parse_long_and_short_aliases()
    {
        var spec = CliOptionSpec.Parse("--name|-n");

        Assert.Equal(new[] { "--name", "-n" }, spec.Aliases);   // aliases keep their leading dashes
        Assert.Equal(new[] { "name" }, spec.LongOptionNames);   // long/short names are dash-stripped
        Assert.Equal(new[] { "n" }, spec.ShortOptionNames);
        Assert.Equal("--name|-n", spec.PipeSeparatedAliases);
    }

    [Fact]
    public void Order_pipe_separated_long_first_then_longest_within_group()
    {
        var spec = CliOptionSpec.Parse("-v|--verbose|--v");

        Assert.Equal("--verbose|--v|-v", spec.PipeSeparatedAliases);
    }

    [Fact]
    public void Match_aliases_case_insensitively()
    {
        var spec = CliOptionSpec.Parse("--verbose|-v");

        Assert.True(spec.IsMatch("--verbose"));
        Assert.True(spec.IsMatch("--VERBOSE"));   // matcher is case-insensitive
        Assert.True(spec.IsMatch("-v"));
        Assert.False(spec.IsMatch("--other"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_empty_specification(string specification) =>
        Assert.ThrowsAny<ArgumentException>(() => CliOptionSpec.Parse(specification));

    [Fact]
    public void Reject_specification_without_a_leading_dash() =>
        Assert.Throws<ArgumentException>(() => CliOptionSpec.Parse("noleadingdash"));

    [Fact]
    public void Reject_duplicate_alias() =>
        Assert.Throws<ArgumentException>(() => CliOptionSpec.Parse("--name|--name"));
}

using System.Collections.Generic;
using System.Linq;
using Portico;
using Portico.Testing;
using Xunit;

namespace AdminCli.Tests;

/// <summary>
/// The wedge.
///
/// Every <c>[CliCommandExample]</c> on <see cref="IAdminTool"/> is run through the real pipeline
/// against a <c>DispatchProxy</c> of the interface. An example that no longer dispatches — because
/// someone renamed a route, changed an option, or made an argument required — fails the build.
/// The documentation cannot drift from the code, because the documentation IS the test.
/// </summary>
public sealed class AdminContract_Should
{
    private static IReadOnlyList<CliContractExample> Contract =>
        new CliContractValidator<IAdminTool>().Enumerate();

    // One test case per example: "3 of 12 failed", not one red blob.
    public static IEnumerable<object[]> Examples() =>
        new CliContractValidator<IAdminTool>()
            .Enumerate()
            .Select(example => new object[] { example });

    [Theory]
    [MemberData(nameof(Examples))]
    public void Dispatch(CliContractExample example) =>
        // FailureReason is the framework's own diagnostic — "Unrecognized option(s): --bogus",
        // "Value 'abc' for option '--amount' is invalid." Without it, a red build tells you an
        // example broke and leaves you to find out why.
        Assert.True(
            example.Matched,
            $"Example did not dispatch: {example.Example}{System.Environment.NewLine}" +
            $"  Reason: {example.FailureReason}");

    // Dispatching is the floor, not the ceiling. `Matched` only says the example reached *some*
    // route — it would stay green if the example silently began reaching a different handler, or
    // binding a different value. Pinning the handler and the bound arguments is what turns an
    // example into a contract.

    [Fact]
    public void Bind_The_Row_Count_As_An_Int()
    {
        var seed = Contract.Single(e => e.Example == "db seed --rows 100");

        Assert.Equal(nameof(IAdminTool.Seed), seed.Handler);
        Assert.Equal(100, seed.Arguments["rows"]);
    }

    [Fact]
    public void Default_The_Row_Count_When_It_Is_Omitted()
    {
        var seed = Contract.Single(e => e.Example == "db seed");

        Assert.Equal(nameof(IAdminTool.Seed), seed.Handler);
        Assert.Equal(10, seed.Arguments["rows"]);
    }

    [Fact]
    public void Bind_The_Optional_Trailing_Positional_To_Its_Csharp_Default()
    {
        // "reindex" with no index: the C# default ("all") binds, and the route still reaches Reindex.
        var bare = Contract.Single(e => e.Example == "reindex");
        Assert.Equal(nameof(IAdminTool.Reindex), bare.Handler);
        Assert.Equal("all", bare.Arguments["index"]);

        var named = Contract.Single(e => e.Example == "reindex orders");
        Assert.Equal("orders", named.Arguments["index"]);
    }

    [Fact]
    public void Bind_A_Map_Option_Into_A_Dictionary()
    {
        var sharded = Contract.Single(e => e.Example == "reindex orders --shard[eu] 3 --shard[us] 5");

        Assert.Equal(nameof(IAdminTool.Reindex), sharded.Handler);
        var shard = Assert.IsType<Dictionary<string, int>>(sharded.Arguments["shard"]);
        Assert.Equal(3, shard["eu"]);
        Assert.Equal(5, shard["us"]);
    }

    [Fact]
    public void Bind_A_Human_Readable_TimeSpan()
    {
        var drain = Contract.Single(e => e.Example == "drain --timeout \"30 seconds\"");

        Assert.Equal(nameof(IAdminTool.DrainAsync), drain.Handler);
        Assert.Equal(System.TimeSpan.FromSeconds(30), drain.Arguments["timeout"]);
    }
}

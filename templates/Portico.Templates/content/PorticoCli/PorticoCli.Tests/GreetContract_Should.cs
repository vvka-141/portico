using Portico.Testing;
using Xunit;

namespace PorticoCli.Tests;

/// <summary>
/// This is why Portico exists.
///
/// Every <c>[CliCommandExample]</c> on <see cref="IGreetTool"/> is run through the real pipeline
/// against a <c>DispatchProxy</c> of the interface. Rename a route, make an option required, retype
/// one — and the example stops dispatching, and this build goes red. The documentation cannot drift
/// from the code, because the documentation IS the test.
///
/// Try it: change <c>--name</c> to <c>--who</c> in IGreetTool and run `dotnet test`.
/// </summary>
public sealed class GreetContract_Should
{
    private static IReadOnlyList<CliContractExample> Contract =>
        new CliContractValidator<IGreetTool>().Enumerate();

    // One test case per example: "1 of 2 failed", not one red blob.
    public static IEnumerable<object[]> Examples() =>
        new CliContractValidator<IGreetTool>()
            .Enumerate()
            .Select(example => new object[] { example });

    [Theory]
    [MemberData(nameof(Examples))]
    public void Dispatch(CliContractExample example) =>
        Assert.True(
            example.Matched,
            $"Example did not dispatch: {example.Example}{Environment.NewLine}" +
            $"  Reason: {example.FailureReason}");

    // Dispatching is the floor, not the ceiling. Pin the handler and the bound values and the example
    // becomes a contract: it can no longer reach a different method, or bind a different value, and
    // stay green.
    [Fact]
    public void Bind_The_Name_And_The_Flag()
    {
        var loud = Contract.Single(e => e.Example == "greet --name Grace --loud");

        Assert.Equal(nameof(IGreetTool.Greet), loud.Handler);
        Assert.Equal("Grace", loud.Arguments["name"]);
        Assert.NotNull(loud.Arguments["loud"]);
    }
}

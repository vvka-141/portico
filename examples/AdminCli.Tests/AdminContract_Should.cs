using System.Collections.Generic;
using System.Linq;
using Portico;
using Portico.Testing;
using Xunit;

namespace AdminCli.Tests;

/// <summary>
/// The wedge, in nine lines of test code.
///
/// Every <c>[CliCommandExample]</c> on <see cref="IAdminTool"/> is run through the real pipeline
/// against a <c>DispatchProxy</c> of the interface. An example that no longer dispatches — because
/// someone renamed a route, changed an option, or made an argument required — fails the build.
/// The documentation cannot drift from the code, because the documentation IS the test.
/// </summary>
public sealed class AdminContract_Should
{
    // One test case per example: "3 of 12 failed", not one red blob.
    public static IEnumerable<object[]> Examples() =>
        new CliContractValidator<IAdminTool>()
            .Enumerate()
            .Select(example => new object[] { example });

    [Theory]
    [MemberData(nameof(Examples))]
    public void Dispatch(CliContractExample example) =>
        Assert.True(example.Matched, $"Example did not dispatch: {example.Example}");
}

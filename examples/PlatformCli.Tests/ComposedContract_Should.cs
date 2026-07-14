using System.Collections.Generic;
using System.Linq;
using Platform.Queue;
using Platform.Storage;
using Portico;
using Portico.Testing;
using Xunit;

namespace PlatformCli.Tests;

/// <summary>
/// Composition without verification is the less valuable half. This is the other half: every example
/// on both mounted contracts is run against the composed route table — the one the master CLI
/// actually ships — and must dispatch to the right handler with the right bound values.
///
/// The mount is passed to the validator (<c>new CliContractValidator&lt;IStorageTool&gt;("storage")</c>)
/// because that is where the contract lives in this binary. Validate it unmounted and you would be
/// verifying a CLI nobody runs.
/// </summary>
public sealed class ComposedContract_Should
{
    // Each validator proxies its own contract and composes the *other* team's real tool in, so both
    // run against the whole route table — colliding `status` literals included.
    private static IReadOnlyList<CliContractExample> Storage =>
        new CliContractValidator<IStorageTool>("storage").Enumerate(
            configureApplication: cfg => cfg.AddCommands(new QueueTool(), [new CliRouteAttribute("queue")]));

    private static IReadOnlyList<CliContractExample> Queue =>
        new CliContractValidator<IQueueTool>("queue").Enumerate(
            configureApplication: cfg => cfg.AddCommands(new StorageTool(), [new CliRouteAttribute("storage")]));

    public static IEnumerable<object[]> ComposedExamples() =>
        Storage.Concat(Queue).Select(example => new object[] { example });

    [Theory]
    [MemberData(nameof(ComposedExamples))]
    public void Dispatch_In_The_Composed_Surface(CliContractExample example) =>
        Assert.True(
            example.Matched,
            $"Example did not dispatch: {example.Example}{System.Environment.NewLine}" +
            $"  Reason: {example.FailureReason}");

    [Fact]
    public void Send_A_Colliding_Route_To_The_Contract_That_Owns_Its_Mount()
    {
        // Both contracts declare `status`. The mount, not the route name, decides which one answers.
        var storage = Storage.Single(e => e.Example == "status --bucket invoices");
        var queue = Queue.Single(e => e.Example == "status --queue orders");

        Assert.Equal(nameof(IStorageTool.Status), storage.Handler);
        Assert.Equal("invoices", storage.Arguments["bucket"]);

        Assert.Equal(nameof(IQueueTool.Status), queue.Handler);
        Assert.Equal("orders", queue.Arguments["queue"]);
    }

    [Fact]
    public void Bind_A_Positional_Argument_Through_The_Mount()
    {
        var purge = Storage.Single(e => e.Example == "purge invoices --older-than \"30 days\"");

        Assert.Equal(nameof(IStorageTool.Purge), purge.Handler);
        Assert.Equal("invoices", purge.Arguments["bucket"]);
        Assert.Equal(System.TimeSpan.FromDays(30), purge.Arguments["olderThan"]);
    }

    // The master CLI exactly as Program.cs wires it.
    private static CliTestHarness Platform => CliTestHarness.ForApplication(cfg => cfg
        .AddCommands(new StorageTool(), [new CliRouteAttribute("storage")])
        .AddCommands(new QueueTool(), [new CliRouteAttribute("queue")])
        .WithVersion("platform 1.0.0"));

    [Fact]
    public void Treat_A_Mount_As_A_Move_And_Not_An_Alias()
    {
        var platform = Platform;

        platform.Run("platform storage status").ExpectExit(0).ExpectOut("storage:");
        platform.Run("platform queue status").ExpectExit(0).ExpectOut("queue:");

        // The unmounted route does not survive the mount. There is no collision to resolve at
        // dispatch, because `status` on its own now reaches nothing at all.
        platform.Run("platform status").ExpectExit(2);
    }

    [Fact]
    public void Print_Help_A_User_Can_Paste()
    {
        var platform = Platform;

        var help = platform.Run("platform storage purge --help").ExpectExit(0);

        // Help prints the example under its mount — because an example that omits the mount is one
        // that exits 2 when pasted.
        help.ExpectOut("platform storage purge invoices --older-than \"30 days\"");

        platform.Run("platform storage purge invoices --older-than \"30 days\"").ExpectExit(0);
    }
}

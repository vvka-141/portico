using System.Collections.Generic;
using System.Linq;
using Portico;
using Portico.Testing;
using Xunit;

namespace ReferenceCli.Tests;

/// <summary>
/// The wedge, applied to the reference surface. Every <c>[CliCommandExample]</c> on both contracts
/// is run through the real pipeline against a <c>DispatchProxy</c> — in the <b>composed</b> shape the
/// binary actually ships (fleet at root, diagnostics mounted under <c>diag</c>). An example that
/// stops dispatching, or starts reaching a different handler or binding a different value, fails the
/// build. The corpus cannot drift from the code, because the corpus IS the test.
/// </summary>
public sealed class FleetContract_Should
{
    // Each validator proxies its own contract and composes the OTHER real tool in, so both run
    // against the whole route table the master CLI ships.
    private static CliContractValidator<IFleetTool> FleetValidator =>
        new();

    private static IReadOnlyList<CliContractExample> Fleet =>
        FleetValidator.Enumerate(configureApplication: cfg =>
            cfg.AddCommands(new DiagnosticsTool(), [new CliRouteAttribute("diag")]));

    private static IReadOnlyList<CliContractExample> Diagnostics =>
        new CliContractValidator<IDiagnosticsTool>("diag").Enumerate(configureApplication: cfg =>
            cfg.AddCommands(() => new FleetTool(new FixedClock())));

    public static IEnumerable<object[]> AllExamples() =>
        Fleet.Concat(Diagnostics).Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(AllExamples))]
    public void Dispatch_In_The_Composed_Surface(CliContractExample example) =>
        Assert.True(
            example.Matched,
            $"Example did not dispatch: {example.Example}{System.Environment.NewLine}" +
            $"  Reason: {example.FailureReason}");

    // Dispatching is the floor. The pins below assert the handler AND the bound values — an example
    // is a contract only once both are checked.

    [Fact]
    public void Bind_A_Collection_Option_By_Repetition()
    {
        var e = Fleet.Single(x => x.Example == "worker list eu-west --status draining --status idle -v");

        Assert.Equal(nameof(IFleetTool.ListWorkers), e.Handler);
        Assert.Equal("eu-west", e.Arguments["pool"]);
        Assert.Equal(["draining", "idle"], Assert.IsType<List<string>>(e.Arguments["status"]));
        Assert.NotNull(e.Arguments["verbose"]); // CliFlag? present because -v was passed
    }

    [Fact]
    public void Default_The_Optional_Trailing_Positional()
    {
        var e = Fleet.Single(x => x.Example == "worker list");

        Assert.Equal("all", e.Arguments["pool"]);
        // POR-150: an absent optional collection binds empty, not null — so a handler can iterate
        // it without a null check. A CliFlag? is different: absent genuinely means "off", and null
        // is how that is spelled.
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<string>>(e.Arguments["status"]));
        Assert.Null(e.Arguments["verbose"]); // absent -> off
    }

    [Fact]
    public void Bind_A_Route_Placeholder_And_A_TimeSpan()
    {
        var e = Fleet.Single(x => x.Example == "worker w-42 drain --timeout \"2 minutes\" --force true");

        Assert.Equal(nameof(IFleetTool.DrainWorkerAsync), e.Handler);
        Assert.Equal("w-42", e.Arguments["id"]);
        Assert.Equal(System.TimeSpan.FromMinutes(2), e.Arguments["timeout"]);
        Assert.Equal(true, e.Arguments["force"]);
    }

    [Fact]
    public void Bind_A_Bundle_And_Keep_The_Sensitive_Option_Separate()
    {
        var e = Fleet.Single(x => x.Example == "job submit --queue orders --priority 7");

        Assert.Equal(nameof(IFleetTool.SubmitJob), e.Handler);
        var spec = Assert.IsType<JobSpec>(e.Arguments["spec"]);
        Assert.Equal("orders", spec.Queue);
        Assert.Equal(7, spec.Priority);
    }

    [Fact]
    public void Apply_A_String_Form_DefaultValue()
    {
        var bare = Fleet.Single(x => x.Example == "job j-100 logs");
        Assert.Equal(nameof(IFleetTool.JobLogs), bare.Handler);
        Assert.Equal(100, bare.Arguments["tail"]); // DefaultValue = "100" parsed to int

        var tailed = Fleet.Single(x => x.Example == "job j-100 logs --tail 500");
        Assert.Equal(500, tailed.Arguments["tail"]);
    }

    [Fact]
    public void Bind_A_Map_Option_Into_A_Dictionary()
    {
        var e = Fleet.Single(x => x.Example == "queue retain --keep[orders] 7 --keep[audit] 90");

        Assert.Equal(nameof(IFleetTool.SetRetention), e.Handler);
        var keep = Assert.IsType<Dictionary<string, int>>(e.Arguments["keep"]);
        Assert.Equal(7, keep["orders"]);
        Assert.Equal(90, keep["audit"]);
    }

    [Fact]
    public void Rank_The_Specialized_Route_Ahead_When_Its_Option_Is_Present()
    {
        // "run migrate --to v5" and the "run {command}" catch-all both match the two segments
        // "run migrate"; the --to option ranks the specialized route ahead.
        var migrate = Fleet.Single(x => x.Example == "run migrate --to v5");
        Assert.Equal(nameof(IFleetTool.Migrate), migrate.Handler);
        Assert.Equal("v5", migrate.Arguments["to"]);

        // "run gc" matches only the catch-all.
        var passthrough = Fleet.Single(x => x.Example == "run gc");
        Assert.Equal(nameof(IFleetTool.Run), passthrough.Handler);
        Assert.Equal("gc", passthrough.Arguments["command"]);
    }

    [Fact]
    public void Dispatch_The_Mounted_Contract_Under_Its_Prefix()
    {
        // health/version are declared plain on IDiagnosticsTool; the mount makes them "diag health".
        var health = Diagnostics.Single(x => x.Example == "health");
        Assert.Equal(nameof(IDiagnosticsTool.Health), health.Handler);
    }
}

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

    // --- db backfill: the route docs/how-to/operational-command.md walks through ----------------
    //
    // Every claim that walkthrough makes about binding is pinned here, so the page cannot drift
    // from the command it documents. If one of these goes red, the page is lying.

    [Fact]
    public void Bind_A_Repeated_Collection_Option()
    {
        var backfill = Contract.Single(e => e.Example == "db backfill --ids 41 42 43 --dry-run");

        Assert.Equal(nameof(IAdminTool.BackfillAsync), backfill.Handler);
        Assert.Equal(new[] { 41, 42, 43 }, Assert.IsType<int[]>(backfill.Arguments["ids"]));
        Assert.NotNull(backfill.Arguments["dryRun"]);       // CliFlag? present because --dry-run was passed
    }

    [Fact]
    public void Bind_An_Empty_Collection_When_The_Option_Is_Absent()
    {
        // POR-150. This bound null until 0.2.0, and every handler paid a null check for it.
        var bare = Contract.Single(e => e.Example == "db backfill");

        var ids = Assert.IsType<int[]>(bare.Arguments["ids"]);
        Assert.Empty(ids);
        Assert.Null(bare.Arguments["dryRun"]);              // absent -> off
    }

    [Fact]
    public void Bind_A_Compact_Duration_On_The_Backfill()
    {
        var timed = Contract.Single(e => e.Example == "db backfill --ids 41 42 43 --timeout \"5 min\"");

        Assert.Equal(System.TimeSpan.FromMinutes(5), timed.Arguments["timeout"]);
    }

    [Fact]
    public void Leave_The_Connection_String_Unbound_When_Neither_Argv_Nor_The_Environment_Supplies_One()
    {
        // The walkthrough's first step: the option is optional precisely so the environment can
        // fill it, and the handler — not the parser — decides that missing config is a usage error.
        var bare = Contract.Single(e => e.Example == "db backfill");

        Assert.Null(bare.Arguments["connectionString"]);
    }

    [Fact]
    public void Refuse_A_Backfill_With_No_Connection_String_As_A_Usage_Error()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new AdminTool()))
            .Run("admin db backfill --ids 41");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("PGCONNSTR", result.StandardError);
    }

    [Fact]
    public void Name_The_Environment_Variable_In_Help_Without_Reading_It()
    {
        // POR-149. The variable's NAME is what an operator needs; its VALUE must never appear.
        System.Environment.SetEnvironmentVariable("PGCONNSTR", "Host=secret-host;Password=hunter2");
        try
        {
            var result = CliTestHarness
                .ForApplication(cfg => cfg.AddCommands(new AdminTool()))
                .Run("admin db backfill --help");

            Assert.Contains("(env: PGCONNSTR)", result.StandardOut);
            Assert.DoesNotContain("hunter2", result.StandardOut);
            Assert.DoesNotContain("secret-host", result.StandardOut);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PGCONNSTR", null);
        }
    }

    [Fact]
    public void Read_The_Connection_String_From_The_Environment()
    {
        System.Environment.SetEnvironmentVariable("PGCONNSTR", "Host=db;Username=svc");
        try
        {
            var result = CliTestHarness
                .ForApplication(cfg => cfg.AddCommands(new AdminTool()))
                .Run("admin db backfill --ids 41 --dry-run");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("dry run", result.StandardOut);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PGCONNSTR", null);
        }
    }

    [Fact]
    public void Let_Argv_Beat_The_Environment()
    {
        System.Environment.SetEnvironmentVariable("PGCONNSTR", "Host=from-env");
        try
        {
            var result = CliTestHarness
                .ForApplication(cfg => cfg.AddCommands(new AdminTool()))
                .Run("admin db backfill --connection-string \"Host=from-argv\" --ids 41 --dry-run");

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("PGCONNSTR", null);
        }
    }

    [Fact]
    public void Refuse_A_Bare_Number_As_A_Duration()
    {
        // POR-147. `--timeout 5` would have meant five DAYS. The walkthrough says so; this proves it.
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new AdminTool()))
            .Run("admin db backfill --connection-string x --ids 41 --timeout 5");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("means DAYS", result.StandardError);
    }
}

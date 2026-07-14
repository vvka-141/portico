using System;
using System.Collections.Generic;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// CliContractValidator<T> is the framework's signature feature per CHARTER §4.2: every
// [CliCommandExample] on a contract is an executable test case. These tests verify the
// validator end-to-end through DispatchProxy on a synthetic contract.
public sealed class CliContractValidator_Should
{
    public interface IDeployContract
    {
        [CliRoute("deploy {env}")]
        [CliCommandExample("deploy prod")]
        [CliCommandExample("deploy staging --dry-run")]
        int Deploy(string env, [CliOption("--dry-run")] CliFlag? dryRun = null);
    }

    [Fact]
    public void Treat_Every_Matching_Example_As_Invoked()
    {
        var invoked = new List<string>();
        var notInvoked = new List<string>();

        new CliContractValidator<IDeployContract>().Validate(
            onNotInvoked: ex => notInvoked.Add(ex.Example),
            onInvoked: ex => invoked.Add(ex.Example));

        Assert.Equal(2, invoked.Count);
        Assert.Empty(notInvoked);
        Assert.Contains("deploy prod", invoked);
        Assert.Contains("deploy staging --dry-run", invoked);
    }

    public interface IBrokenContract
    {
        // 'deploy bad too-many-segments' has too many segments — won't match `deploy {env}`.
        [CliRoute("deploy {env}")]
        [CliCommandExample("deploy prod")]
        [CliCommandExample("deploy bad too-many-segments")]
        int Deploy(string env);
    }

    [Fact]
    public void Surface_Examples_That_Do_Not_Match_The_Contract()
    {
        var invoked = new List<string>();
        var notInvoked = new List<string>();

        new CliContractValidator<IBrokenContract>().Validate(
            onNotInvoked: ex => notInvoked.Add(ex.Example),
            onInvoked: ex => invoked.Add(ex.Example));

        Assert.Single(invoked);
        Assert.Contains("deploy prod", invoked);
        Assert.Single(notInvoked);
        Assert.Contains("deploy bad too-many-segments", notInvoked);
    }

    public sealed class NotAnInterface
    {
        [CliRoute("noop")]
        [CliCommandExample("noop")]
        public int Noop() => 0;
    }

    [Fact]
    public void Throw_When_T_Is_Not_An_Interface()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new CliContractValidator<NotAnInterface>().Validate(_ => { }));
        Assert.Contains("requires T to be an interface", ex.Message);
    }

    public interface IContractWithoutExamples
    {
        [CliRoute("noop")]
        int Noop();
    }

    [Fact]
    public void Throw_When_Contract_Has_No_Examples()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new CliContractValidator<IContractWithoutExamples>().Validate(_ => { }));
        Assert.Contains("[CliCommandExample]", ex.Message);
    }

    public interface IMultiMethodContract
    {
        [CliRoute("init {path}")]
        [CliCommandExample("init .")]
        int Init(string path);

        [CliRoute("clean")]
        [CliCommandExample("clean")]
        int Clean();
    }

    [Fact]
    public void Check_Examples_Across_Every_Method_Independently()
    {
        var invoked = new List<string>();

        new CliContractValidator<IMultiMethodContract>().Validate(
            onNotInvoked: ex => Assert.Fail($"Unexpected miss on '{ex.Example}'"),
            onInvoked: ex => invoked.Add(ex.Example));

        Assert.Equal(2, invoked.Count);
        Assert.Contains("init .", invoked);
        Assert.Contains("clean", invoked);
    }

    // --- Per-example enumeration (SOL-87) ----------------------------------------------------

    [Fact]
    public void Enumerate_Returns_One_Descriptor_Per_Example_With_Its_Match_Result()
    {
        var results = new CliContractValidator<IDeployContract>().Enumerate();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Matched));
        Assert.Contains(results, r => r.Example == "deploy prod");
        Assert.Contains(results, r => r.Example == "deploy staging --dry-run");
    }

    [Fact]
    public void Enumerate_Flags_A_Non_Matching_Example_As_Unmatched()
    {
        var results = new CliContractValidator<IBrokenContract>().Enumerate();

        Assert.Equal(2, results.Count);
        Assert.True(Assert.Single(results, r => r.Example == "deploy prod").Matched);
        Assert.False(Assert.Single(results, r => r.Example == "deploy bad too-many-segments").Matched);
    }

    [Fact]
    public void Enumerate_Throws_On_The_Same_Contract_Errors_As_Validate()
    {
        Assert.Throws<InvalidOperationException>(
            () => new CliContractValidator<NotAnInterface>().Enumerate());
        Assert.Throws<InvalidOperationException>(
            () => new CliContractValidator<IContractWithoutExamples>().Enumerate());
    }

    // --- The example is a contract, not a smoke test (POR-38) ---------------------------------
    //
    // `Matched` only says the example reached *some* route. On its own that is a routability
    // check: an example could silently start dispatching to a different overload, or binding a
    // different value, and still report a pass. Handler + Arguments are what make it a contract.

    [Fact]
    public void Report_Which_Handler_An_Example_Dispatched_To()
    {
        var results = new CliContractValidator<IMultiMethodContract>().Enumerate();

        Assert.Equal(nameof(IMultiMethodContract.Init),
            Assert.Single(results, r => r.Example == "init .").Handler);
        Assert.Equal(nameof(IMultiMethodContract.Clean),
            Assert.Single(results, r => r.Example == "clean").Handler);
    }

    [Fact]
    public void Report_The_Values_Bound_To_The_Handler()
    {
        var results = new CliContractValidator<IDeployContract>().Enumerate();

        var prod = Assert.Single(results, r => r.Example == "deploy prod");
        Assert.Equal("prod", prod.Arguments["env"]);
        Assert.Null(prod.Arguments["dryRun"]);            // the C# default, not supplied

        var staging = Assert.Single(results, r => r.Example == "deploy staging --dry-run");
        Assert.Equal("staging", staging.Arguments["env"]);
        Assert.NotNull(staging.Arguments["dryRun"]);      // the flag was present
    }

    [Fact]
    public void Report_No_Handler_And_No_Arguments_When_Nothing_Dispatched()
    {
        var miss = Assert.Single(
            new CliContractValidator<IBrokenContract>().Enumerate(),
            r => r.Example == "deploy bad too-many-segments");

        Assert.False(miss.Matched);
        Assert.Null(miss.Handler);
        Assert.Empty(miss.Arguments);
    }

    public interface ISeedContract
    {
        [CliRoute("db seed")]
        [CliCommandExample("db seed --rows 100")]
        int Seed([CliOption("--rows")] int rows = 10);
    }

    // The binding is pinned by TYPE and VALUE, not just "it parsed". Retype `--rows` from int to
    // string and the example still dispatches — `Matched` stays true and an exit-code-only
    // validator stays green — but the bound value silently becomes "100" instead of 100. That
    // change of contract is invisible without this assertion, and it is the whole of POR-38.
    [Fact]
    public void Pin_The_Bound_Value_By_Type_Not_Just_By_Parse()
    {
        var seed = Assert.Single(new CliContractValidator<ISeedContract>().Enumerate());

        Assert.True(seed.Matched);
        Assert.Equal(nameof(ISeedContract.Seed), seed.Handler);

        // Assert.Equal(100, ...) on an object? is a boxed-int comparison: "100" would not satisfy it.
        Assert.Equal(100, seed.Arguments["rows"]);
        Assert.IsType<int>(seed.Arguments["rows"]);
    }
}

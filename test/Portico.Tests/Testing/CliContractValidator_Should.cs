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
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-151. A map option bound one value per key, and a repeated key was a usage error. That is the
// wrong default for the domain: HTTP headers, Kubernetes labels, `docker --label` and `curl -H` all
// repeat keys as a matter of course.
//
// The framework's own metaphor is the strongest argument. Map options exist because `?cfg[env]=prod`
// is a query string — and `?tag=a&tag=b` is canonical query-string form, which ASP.NET Core binds to
// string[] without ceremony. The single-value restriction was the part with no expression in the
// metaphor.
//
// THE DECLARED VALUE TYPE CHOOSES THE SEMANTICS. Dictionary<string,T> still rejects a repeated key;
// Dictionary<string,T[]> accumulates. No new attribute, no new syntax.
public sealed class CliMultiValuedMapKey_Should
{
    public sealed class ArrayValues
    {
        public Dictionary<string, string[]>? Received;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run([CliOption("--h")] Dictionary<string, string[]>? h = null)
        {
            Received = h;
            return 0;
        }
    }

    private static Dictionary<string, string[]> Bind(string commandLine)
    {
        var svc = new ArrayValues();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run(commandLine).ExpectExit(0);
        return svc.Received!;
    }

    [Fact]
    public void Accumulate_Several_Values_Written_After_One_Key()
    {
        var bound = Bind("app run --h[Accept] json html");

        Assert.Equal(new[] { "json", "html" }, bound["Accept"]);
    }

    /// <summary>
    /// The two ways of writing the same thing must agree. `--h[a] x y` arrives as one capture
    /// carrying both values; `--h[a] x --h[a] y` arrives as two captures. Gathering before building
    /// is what makes them identical.
    /// </summary>
    [Fact]
    public void Produce_The_Same_Result_From_Either_Invocation_Form()
    {
        var oneCapture = Bind("app run --h[Accept] json html");
        var twoCaptures = Bind("app run --h[Accept] json --h[Accept] html");

        Assert.Equal(oneCapture["Accept"], twoCaptures["Accept"]);
    }

    [Fact]
    public void Preserve_Key_Order_And_Value_Order_As_Typed()
    {
        var bound = Bind("app run --h[b] 1 --h[a] 2 --h[b] 3");

        Assert.Equal(new[] { "b", "a" }, bound.Keys);
        Assert.Equal(new[] { "1", "3" }, bound["b"]);
        Assert.Equal(new[] { "2" }, bound["a"]);
    }

    /// <summary>
    /// The value-shape rule is orthogonal to the container-shape rule, decided once rather than as a
    /// per-container table. The per-key collection is built before the container conversion, so
    /// every map container POR-144 supports accumulates without knowing this feature exists.
    /// </summary>
    [Fact]
    public void Accumulate_In_Every_Supported_Container()
    {
        Assert.Equal(["x", "y"], BindListValues().Values.Single());
        Assert.Equal(["x", "y"], BindImmutable().Values.Single());
        Assert.Equal(["x", "y"], BindReadOnly().Values.Single());
        Assert.Equal(["x", "y"], BindSorted().Values.Single());
    }

    public sealed class ListValues
    {
        public Dictionary<string, List<string>>? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--h")] Dictionary<string, List<string>>? h = null) { Received = h; return 0; }
    }

    public sealed class ImmutableValues
    {
        public ImmutableDictionary<string, string[]>? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--h")] ImmutableDictionary<string, string[]>? h = null) { Received = h; return 0; }
    }

    public sealed class ReadOnlyValues
    {
        public IReadOnlyDictionary<string, string[]>? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--h")] IReadOnlyDictionary<string, string[]>? h = null) { Received = h; return 0; }
    }

    public sealed class SortedValues
    {
        public SortedDictionary<string, string[]>? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--h")] SortedDictionary<string, string[]>? h = null) { Received = h; return 0; }
    }

    private static Dictionary<string, IEnumerable<string>> BindListValues()
    {
        var svc = new ListValues();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run --h[a] x y").ExpectExit(0);
        return svc.Received!.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value);
    }

    private static Dictionary<string, IEnumerable<string>> BindImmutable()
    {
        var svc = new ImmutableValues();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run --h[a] x y").ExpectExit(0);
        return svc.Received!.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value);
    }

    private static Dictionary<string, IEnumerable<string>> BindReadOnly()
    {
        var svc = new ReadOnlyValues();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run --h[a] x y").ExpectExit(0);
        return svc.Received!.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value);
    }

    private static Dictionary<string, IEnumerable<string>> BindSorted()
    {
        var svc = new SortedValues();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run --h[a] x y").ExpectExit(0);
        return svc.Received!.ToDictionary(kv => kv.Key, kv => (IEnumerable<string>)kv.Value);
    }

    // --- The element type converts through the collection path -------------------------------

    public sealed class IntValues
    {
        public Dictionary<string, int[]>? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--p")] Dictionary<string, int[]>? p = null) { Received = p; return 0; }
    }

    [Fact]
    public void Convert_Each_Element_Through_The_Same_Path_A_Collection_Would()
    {
        var svc = new IntValues();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run --p[a] 1 2").ExpectExit(0);

        Assert.Equal(new[] { 1, 2 }, svc.Received!["a"]);
    }

    [Fact]
    public void Name_The_Key_When_An_Element_Fails_To_Convert()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new IntValues()))
            .Run("app run --p[a] 1 notanint");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("for key 'a'", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("notanint", result.StandardError, StringComparison.Ordinal);
    }

    // --- A single-valued map must NOT silently become last-wins ------------------------------

    public sealed class SingleValued
    {
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--h")] Dictionary<string, string>? h = null) => 0;
    }

    [Fact]
    public void Still_Reject_A_Repeated_Key_On_A_Single_Valued_Map()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new SingleValued()))
            .Run("app run --h[a] x --h[a] y");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("Duplicate key 'a'", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Still_Reject_Several_Values_On_A_Single_Valued_Map()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new SingleValued()))
            .Run("app run --h[a] x y");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("expected a single value", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty key is still a usage error on the multi-valued path — the guard must not have been
    /// bypassed by the new branch.
    /// </summary>
    [Fact]
    public void Still_Reject_An_Empty_Key()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new ArrayValues()))
            .Run("app run --h[] x");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("empty map key", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>ILookup&lt;K,V&gt;</c> is deliberately not supported: it has no public constructor and is
    /// built through <c>Enumerable.ToLookup</c>, so its factory shape matches no branch in
    /// <c>BuildCollectionFactory</c> — real work for a type .NET developers reach for far less often
    /// than <c>string[]</c>. It is refused at <c>Create</c> rather than failing at dispatch, which is
    /// the resting state POR-144 established for any shape the framework does not build.
    /// </summary>
    [Fact]
    public void Refuse_ILookup_At_Startup_Rather_Than_Half_Supporting_It()
    {
        var exception = Assert.Throws<CliConfigurationException>(() =>
            CliTestHarness.ForApplication(cfg => cfg.AddCommands(new LookupValues())).Run("app run"));

        Assert.Contains("--h", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot construct", exception.Message, StringComparison.Ordinal);
    }

    public sealed class LookupValues
    {
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--h")] ILookup<string, string>? h = null) => 0;
    }
}

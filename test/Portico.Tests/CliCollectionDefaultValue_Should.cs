using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-156, found while implementing POR-150. `[CliOption(DefaultValue = "eu,us")]` on a collection
// converted the string through the ELEMENT converter — CanAccept answers on the element for a
// collection type — so a `string` reached a `string[]` parameter and died inside MethodInfo.Invoke
// at exit 1. The POR-144 root cause, one layer down.
//
// Three different broken behaviours, depending on the shape:
//   string[] / List<T>      exit 1, raw BCL type error
//   int[]                   CliConfigurationException saying "1,2 is not a valid value for Int32",
//                           which never mentions that the value was meant to be a list
//   Dictionary<string,V>    SILENTLY IGNORED — bound an empty map, author never told
//
// THE SEMANTICS DECISION: a collection default comma-splits, matching the environment-variable path.
// That is not a new convention — POR-73 already settled the identical problem ("one variable has to
// carry several values, and a comma is the convention every operator already knows") and an authored
// attribute default has exactly the same property: one string, no other way to carry a list, with
// argv as the escape hatch for a value that contains a comma.
//
// A MAP default is refused, for POR-54's reason: every encoding of key/value pairs in one string
// nests one separator inside another and breaks on the first value containing either.
public sealed class CliCollectionDefaultValue_Should
{
    public sealed class ArrayService
    {
        public string[]? Received;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run([CliOption("--regions", DefaultValue = "eu,us")] string[]? regions = null)
        {
            Received = regions;
            return 0;
        }
    }

    [Fact]
    public void Split_A_Collection_Default_On_Commas()
    {
        var svc = new ArrayService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run").ExpectExit(0);

        Assert.Equal(new[] { "eu", "us" }, svc.Received);
    }

    /// <summary>A default is a fallback, not an override — argv still wins.</summary>
    [Fact]
    public void Let_Argv_Beat_The_Default()
    {
        var svc = new ArrayService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run --regions a b c").ExpectExit(0);

        Assert.Equal(new[] { "a", "b", "c" }, svc.Received);
    }

    public sealed class ListService
    {
        public List<string>? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--regions", DefaultValue = "eu,us")] List<string>? regions = null)
        {
            Received = regions;
            return 0;
        }
    }

    public sealed class ImmutableService
    {
        public ImmutableArray<string> Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--regions", DefaultValue = "eu,us")] ImmutableArray<string> regions = default)
        {
            Received = regions;
            return 0;
        }
    }

    public sealed class IntService
    {
        public int[]? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--ports", DefaultValue = "80,443")] int[]? ports = null)
        {
            Received = ports;
            return 0;
        }
    }

    /// <summary>
    /// The rule is about the collection shape, not about one type. Each of these produced a
    /// different failure before.
    /// </summary>
    [Fact]
    public void Apply_To_Every_Collection_Shape()
    {
        var list = new ListService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(list)).Run("app run").ExpectExit(0);
        Assert.Equal(new[] { "eu", "us" }, list.Received);

        var immutable = new ImmutableService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(immutable)).Run("app run").ExpectExit(0);
        Assert.Equal(new[] { "eu", "us" }, immutable.Received.ToArray());

        var ints = new IntService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(ints)).Run("app run").ExpectExit(0);
        Assert.Equal(new[] { 80, 443 }, ints.Received);
    }

    /// <summary>
    /// The bundle-property path resolves defaults through the same helper, and the two paths have
    /// drifted before (POR-59).
    /// </summary>
    public sealed class Bundle : CliOptions
    {
        [CliOption("--regions", DefaultValue = "eu,us")]
        public string[]? Regions { get; set; }
    }

    public sealed class BundleService
    {
        public string[]? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run(Bundle? bundle = null)
        {
            Received = bundle?.Regions;
            return 0;
        }
    }

    [Fact]
    public void Apply_To_A_Bundle_Property_Too()
    {
        var svc = new BundleService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run").ExpectExit(0);

        Assert.Equal(new[] { "eu", "us" }, svc.Received);
    }

    /// <summary>
    /// A bad element is a configuration error at startup, and it names the element rather than the
    /// whole list — "1,2 is not a valid value for Int32" told the author nothing about which part
    /// was wrong, or that the value was being read as a list at all.
    /// </summary>
    [Fact]
    public void Name_The_Offending_Element_At_Startup()
    {
        var exception = Assert.Throws<CliConfigurationException>(() =>
            CliTestHarness.ForApplication(cfg => cfg.AddCommands(new BadElementService())).Run("app run"));

        Assert.Contains("notanint", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("80,notanint", exception.Message, StringComparison.Ordinal);
    }

    public sealed class BadElementService
    {
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--ports", DefaultValue = "80,notanint")] int[]? ports = null) => 0;
    }

    /// <summary>
    /// A map default is refused at <c>Create</c>. It used to be accepted and then silently ignored —
    /// the map bound empty and the author was never told, which is the worst of the three original
    /// behaviours and the reason this is a loud refusal rather than a quiet no-op.
    /// </summary>
    [Fact]
    public void Refuse_A_Map_Default_Loudly()
    {
        var exception = Assert.Throws<CliConfigurationException>(() =>
            CliTestHarness.ForApplication(cfg => cfg.AddCommands(new MapDefaultService())).Run("app run"));

        Assert.Contains("is a map", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DefaultValue is not supported", exception.Message, StringComparison.Ordinal);
    }

    public sealed class MapDefaultService
    {
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--cfg", DefaultValue = "a=1")] Dictionary<string, string>? cfg = null) => 0;
    }

    /// <summary>
    /// A scalar default is untouched — the comma is only a separator where a collection is being
    /// built, so a string option keeps a comma in its value.
    /// </summary>
    [Fact]
    public void Leave_A_Scalar_Default_Alone()
    {
        var svc = new ScalarService();
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(svc)).Run("app run").ExpectExit(0);

        Assert.Equal("eu,us", svc.Received);
    }

    public sealed class ScalarService
    {
        public string? Received;
        [CliRoute("run")] [CliCommandExample("run")]
        public int Run([CliOption("--regions", DefaultValue = "eu,us")] string? regions = null)
        {
            Received = regions;
            return 0;
        }
    }
}

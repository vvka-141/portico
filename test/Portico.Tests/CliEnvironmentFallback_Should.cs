using System;
using System.Collections.Generic;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-3 lists "EnvironmentVariable= fallback is scalar-only" as its highest-value gap. This pins what
// the framework ACTUALLY does today, so the documentation cannot overclaim and a future fix has a
// baseline to change deliberately rather than by accident.
//
// The fallback is implemented in CliScalarOptionMaterializer and nowhere else: CliFlagOptionMaterializer,
// CliCollectionOptionMaterializer and CliDictionaryOptionMaterializer never read the attribute.
public sealed class CliEnvironmentFallback_Should
{
    public sealed class Tool
    {
        public string? Scalar;
        public CliFlag? Flag;
        public List<string>? Collection;
        public Dictionary<string, string>? Map;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--scalar", EnvironmentVariable = "POR_T_SCALAR")] string? scalar = null,
            [CliOption("--flag", EnvironmentVariable = "POR_T_FLAG")] CliFlag? flag = null,
            [CliOption("--item", EnvironmentVariable = "POR_T_ITEMS")] List<string>? items = null,
            [CliOption("--map", EnvironmentVariable = "POR_T_MAP")] Dictionary<string, string>? map = null)
        {
            Scalar = scalar;
            Flag = flag;
            Collection = items;
            Map = map;
            return 0;
        }
    }

    private static void WithEnv(Action body)
    {
        Environment.SetEnvironmentVariable("POR_T_SCALAR", "from-env");
        Environment.SetEnvironmentVariable("POR_T_FLAG", "true");
        Environment.SetEnvironmentVariable("POR_T_ITEMS", "a");
        Environment.SetEnvironmentVariable("POR_T_MAP", "k=v");
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("POR_T_SCALAR", null);
            Environment.SetEnvironmentVariable("POR_T_FLAG", null);
            Environment.SetEnvironmentVariable("POR_T_ITEMS", null);
            Environment.SetEnvironmentVariable("POR_T_MAP", null);
        }
    }

    [Fact]
    public void Honour_The_Environment_For_A_Scalar_Option()
    {
        var tool = new Tool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        WithEnv(() => Assert.Equal(0, app.Run("app run")));

        Assert.Equal("from-env", tool.Scalar);
    }

    [Fact]
    public void Ignore_The_Environment_For_A_Flag_A_Collection_And_A_Map()
    {
        // The gap, pinned. `EnvironmentVariable` on these three shapes is silently inert: the option
        // takes its default as though the variable were unset. A containerized service configuring
        // `--item` or `--map[key]` from the environment gets nothing, with no diagnostic.
        //
        // If a future change makes any of these bind, THIS TEST SHOULD FAIL — and the documentation
        // (docs/reference/capabilities.md) must be updated in the same commit.
        var tool = new Tool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        WithEnv(() => Assert.Equal(0, app.Run("app run")));

        Assert.False(tool.Flag.HasValue);   // POR_T_FLAG=true is ignored
        Assert.Null(tool.Collection);       // POR_T_ITEMS=a is ignored
        Assert.Empty(tool.Map!);            // POR_T_MAP=k=v is ignored (a map defaults to empty, not null)
    }
}

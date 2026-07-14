using System;
using System.Collections.Generic;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-54. `EnvironmentVariable` used to be honoured by the scalar materializer and NOWHERE else: on a
// flag, a collection or a map it was silently inert — the option took its default as though the
// variable were unset, with no diagnostic. For a containerized service, which is the audience, that
// is the worst failure mode available.
//
// This suite previously pinned that gap. It now pins the behaviour that replaced it.
public sealed class CliEnvironmentFallback_Should
{
    public sealed class Tool
    {
        public string? Scalar;
        public CliFlag? Flag;
        public List<string>? Items;

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--scalar", EnvironmentVariable = "POR_T_SCALAR")] string? scalar = null,
            [CliOption("--flag", EnvironmentVariable = "POR_T_FLAG")] CliFlag? flag = null,
            [CliOption("--item", EnvironmentVariable = "POR_T_ITEMS")] List<string>? items = null)
        {
            Scalar = scalar;
            Flag = flag;
            Items = items;
            return 0;
        }
    }

    private static Tool Run(string commandLine, params (string Name, string? Value)[] environment)
    {
        var tool = new Tool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            Assert.Equal(0, app.Run(commandLine));
        }
        finally
        {
            foreach (var (name, _) in environment)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        return tool;
    }

    // --- Scalar (this half already worked) ----------------------------------------------------

    [Fact]
    public void Bind_A_Scalar_From_The_Environment()
    {
        var tool = Run("app run", ("POR_T_SCALAR", "from-env"));

        Assert.Equal("from-env", tool.Scalar);
    }

    [Fact]
    public void Let_The_Command_Line_Beat_The_Environment()
    {
        var tool = Run("app run --scalar from-argv", ("POR_T_SCALAR", "from-env"));

        Assert.Equal("from-argv", tool.Scalar);
    }

    // --- Flag ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("TRUE")]
    [InlineData("anything-else")]
    public void Turn_A_Flag_On_From_The_Environment(string value)
    {
        var tool = Run("app run", ("POR_T_FLAG", value));

        Assert.True(tool.Flag.HasValue);
    }

    [Theory]
    [InlineData("")]        // `docker run -e FOO` and an undefined compose variable both pass FOO=
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("no")]
    public void Leave_A_Flag_Off_For_An_Empty_Or_Falsey_Environment_Value(string value)
    {
        // Set-but-empty is the one that matters: treating "the variable exists" as "the flag is on"
        // would silently enable a flag nobody asked for, on the most common container idiom there is.
        var tool = Run("app run", ("POR_T_FLAG", value));

        Assert.False(tool.Flag.HasValue);
    }

    // --- Collection ----------------------------------------------------------------------------

    [Fact]
    public void Bind_A_Collection_From_A_Comma_Separated_Environment_Value()
    {
        var tool = Run("app run", ("POR_T_ITEMS", "a,b,c"));

        Assert.Equal(["a", "b", "c"], tool.Items);
    }

    [Fact]
    public void Trim_And_Drop_Empty_Items()
    {
        var tool = Run("app run", ("POR_T_ITEMS", " a , ,b "));

        Assert.Equal(["a", "b"], tool.Items);
    }

    [Fact]
    public void Let_The_Command_Line_Beat_The_Environment_For_A_Collection()
    {
        var tool = Run("app run --item x --item y", ("POR_T_ITEMS", "a,b,c"));

        Assert.Equal(["x", "y"], tool.Items);
    }

    [Fact]
    public void Leave_A_Collection_At_Its_Default_For_An_Empty_Environment_Value()
    {
        var tool = Run("app run", ("POR_T_ITEMS", ""));

        Assert.Null(tool.Items);
    }

    // --- Map: declined, LOUDLY ------------------------------------------------------------------

    public sealed class MapTool
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--shard", EnvironmentVariable = "POR_T_MAP")] Dictionary<string, int>? shard = null) => 0;
    }

    [Fact]
    public void Refuse_EnvironmentVariable_On_A_Map_At_Startup()
    {
        // The bug being fixed is a SILENTLY inert attribute. Leaving a quieter version of it behind —
        // a map that simply ignores the variable — would be the same bug with better manners. It
        // throws at Create, before a single command runs.
        var error = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg
                .WithConsole(new StringCliConsole())
                .AddCommands(new MapTool())));

        Assert.Contains("--shard", error.Message, StringComparison.Ordinal);
        Assert.Contains("not supported on map options", error.Message, StringComparison.Ordinal);
    }
}

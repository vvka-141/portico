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

    // The set-but-empty variable contributes nothing, so the option falls through to the
    // absent-and-optional branch — which since POR-150 binds an empty collection, not null.
    [Fact]
    public void Leave_A_Collection_At_Its_Default_For_An_Empty_Environment_Value()
    {
        var tool = Run("app run", ("POR_T_ITEMS", ""));

        Assert.NotNull(tool.Items);
        Assert.Empty(tool.Items);
    }

    // POR-73. The comma is the collection separator on the ENV path only. A value that legitimately
    // contains a comma survives argv (one element) but is split by the environment (two). The split
    // is necessary — one variable has no other way to carry a list — and the divergence is a
    // documented limitation (docs/reference/capabilities.md, CliOptionAttribute.EnvironmentVariable),
    // NOT a promise that the two channels agree. These two tests pin both halves so the contract is
    // executable rather than merely written down.
    [Fact]
    public void Split_A_Comma_In_A_Value_On_The_Environment_Path()
    {
        var tool = Run("app run", ("POR_T_ITEMS", "Smith, John"));

        // The environment cannot express an element containing the separator — it becomes two.
        Assert.Equal(["Smith", "John"], tool.Items);
    }

    [Fact]
    public void Keep_A_Comma_In_A_Value_On_The_Command_Line_Path()
    {
        var tool = Run("app run --item \"Smith, John\"");

        // argv does NOT split on commas, so the same value is one element here. Do not "reconcile"
        // the two paths by splitting argv — it would corrupt every value that contains a comma.
        Assert.Equal(["Smith, John"], tool.Items);
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

    // --- POR-161: a set-but-empty variable means ABSENT, for every shape -------------------------
    //
    // The three paths disagreed, each having decided locally and only one having written its reason
    // down. The flag path chose "off" and said why; the collection path reached the same answer by
    // accident, as a side effect of dropping empty items after a split; the scalar path bound the
    // empty string. So `PORT=` on an `int` option failed the PROCESS with a usage error and never
    // reached its declared default of 8080.
    //
    // `docker run -e FOO` passes `FOO=`, and so does a compose file interpolating a variable nobody
    // set. Portico's audience is the admin CLI inside a service's container, which makes that the
    // mainline case — a tool that refuses to start because the orchestrator passed an empty string
    // is failing at the worst possible moment, for a reason its operator did not choose.
    //
    // The rule now lives in ONE place, CliOptionMaterializer.EnvironmentValue, so the shapes cannot
    // drift apart again. These tests are the proof that all four agree.

    public sealed class DefaultsTool
    {
        public int Port;
        public string? Name;
        public List<string>? Tags;
        public CliFlag? Verbose;

        [CliRoute("serve")]
        [CliCommandExample("serve")]
        public int Serve(
            [CliOption("--port", EnvironmentVariable = "POR161_PORT")] int port = 8080,
            [CliOption("--name", EnvironmentVariable = "POR161_NAME")] string name = "fallback",
            [CliOption("--tag", EnvironmentVariable = "POR161_TAGS")] List<string>? tags = null,
            [CliOption("--verbose", EnvironmentVariable = "POR161_VERBOSE")] CliFlag? verbose = null)
        {
            Port = port;
            Name = name;
            Tags = tags;
            Verbose = verbose;
            return 0;
        }
    }

    private static (int ExitCode, DefaultsTool Tool) Serve(params (string Name, string? Value)[] environment)
    {
        var tool = new DefaultsTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        foreach (var (name, value) in environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        try
        {
            return (app.Run("app serve"), tool);
        }
        finally
        {
            foreach (var (name, _) in environment)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    /// <summary>
    /// The sharp one from the ticket: an empty variable on a value-typed option used to fail the
    /// process, because "" is not an Int32 and the declared default was never consulted.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Fall_Back_To_The_Declared_Default_For_A_Value_Type(string empty)
    {
        var (exitCode, tool) = Serve(("POR161_PORT", empty));

        Assert.Equal(0, exitCode);
        Assert.Equal(8080, tool.Port);
    }

    /// <summary>
    /// A string option keeps its declared default too, rather than binding the empty string.
    /// </summary>
    /// <remarks>
    /// This is the half of the decision that gives something up: the environment can no longer say
    /// "explicitly empty". Nothing becomes unexpressible — argv still says it — and a variable is a
    /// source of defaults, where an empty answer is not an answer.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Keep_The_Declared_Default_For_A_String(string empty)
    {
        var (exitCode, tool) = Serve(("POR161_NAME", empty));

        Assert.Equal(0, exitCode);
        Assert.Equal("fallback", tool.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Leave_A_Collection_At_Its_Default(string empty)
    {
        var (exitCode, tool) = Serve(("POR161_TAGS", empty));

        Assert.Equal(0, exitCode);
        Assert.Empty(tool.Tags!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Leave_A_Flag_Off(string empty)
    {
        var (exitCode, tool) = Serve(("POR161_VERBOSE", empty));

        Assert.Equal(0, exitCode);
        Assert.Null(tool.Verbose);
    }

    /// <summary>
    /// The rule is about EMPTY, not about the environment fallback itself — a non-empty variable
    /// still wins over the declared default, on every shape.
    /// </summary>
    /// <remarks>
    /// Without this, "empty means absent" could be implemented as "ignore the environment entirely"
    /// and every test above would still pass. It is the assertion that keeps the fix from being a
    /// removal.
    /// </remarks>
    [Fact]
    public void Still_Let_A_Non_Empty_Variable_Win()
    {
        var (exitCode, tool) = Serve(
            ("POR161_PORT", "9090"),
            ("POR161_NAME", "from-env"),
            ("POR161_TAGS", "a,b"),
            ("POR161_VERBOSE", "1"));

        Assert.Equal(0, exitCode);
        Assert.Equal(9090, tool.Port);
        Assert.Equal("from-env", tool.Name);
        Assert.Equal(["a", "b"], tool.Tags);
        Assert.NotNull(tool.Verbose);
    }

    /// <summary>argv still outranks a non-empty variable — the precedence POR-54 established.</summary>
    [Fact]
    public void Still_Let_Argv_Win_Over_The_Environment()
    {
        var tool = new DefaultsTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        Environment.SetEnvironmentVariable("POR161_PORT", "9090");
        try
        {
            Assert.Equal(0, app.Run("app serve --port 7070"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("POR161_PORT", null);
        }

        Assert.Equal(7070, tool.Port);
    }
}

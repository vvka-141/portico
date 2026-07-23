using System;
using System.Collections.Generic;
using System.IO;
using Portico.Completion;
using Portico.Testing;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// POR-46. Every capability claimed in docs/reference/capabilities.md is exercised here, end to end,
// through the real pipeline. The page is written FROM these tests, not from a reading of the source
// — the ticket it came from carried a confidently-worded capability ("RankByOptions does overload
// selection") that the framework flatly rejects, filed from a grep of a method name. A capability
// doc with no executable proof is how that happens.
//
// If one of these goes red, the documentation is now lying. Fix the doc or fix the framework; do not
// weaken the test.
public sealed class CliCapabilities_Should
{
    // --- Environment-variable fallback -------------------------------------------------------

    public sealed class EnvTool
    {
        public string? Seen;

        [CliRoute("deploy")]
        [CliCommandExample("deploy")]
        public int Deploy(
            [CliOption("--token", "API token", EnvironmentVariable = "PORTICO_TEST_TOKEN")] string? token = null)
        {
            Seen = token;
            return 0;
        }
    }

    [Fact]
    public void Fall_Back_To_An_Environment_Variable()
    {
        var tool = new EnvTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        Environment.SetEnvironmentVariable("PORTICO_TEST_TOKEN", "from-env");
        try
        {
            Assert.Equal(0, app.Run("app deploy"));
            Assert.Equal("from-env", tool.Seen);

            // ...and the command line still wins over the environment.
            Assert.Equal(0, app.Run("app deploy --token from-argv"));
            Assert.Equal("from-argv", tool.Seen);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTICO_TEST_TOKEN", null);
        }
    }

    // capabilities.md claims a collection's environment value is comma-separated while argv is not.
    // Pin that documented divergence here — this suite is what tells us the page has started lying.
    public sealed class EnvCollectionTool
    {
        public List<string>? Seen;

        [CliRoute("tag")]
        [CliCommandExample("tag")]
        public int Tag(
            [CliOption("--tag", "labels", EnvironmentVariable = "PORTICO_TEST_TAGS")] List<string>? tags = null)
        {
            Seen = tags;
            return 0;
        }
    }

    [Fact]
    public void Split_A_Collection_Environment_Value_On_Commas_But_Not_Argv()
    {
        var tool = new EnvCollectionTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        Environment.SetEnvironmentVariable("PORTICO_TEST_TAGS", "Smith, John");
        try
        {
            // Environment: comma is the separator, so a comma-bearing value becomes two elements.
            Assert.Equal(0, app.Run("app tag"));
            Assert.Equal(["Smith", "John"], tool.Seen);

            // argv: no split, so the same value is one element — the documented divergence.
            Assert.Equal(0, app.Run("app tag --tag \"Smith, John\""));
            Assert.Equal(["Smith, John"], tool.Seen);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PORTICO_TEST_TAGS", null);
        }
    }

    // --- DefaultValue (the string form, distinct from a C# default) ---------------------------

    public sealed class DefaultTool
    {
        public int Seen;

        [CliRoute("seed")]
        [CliCommandExample("seed")]
        public int Seed([CliOption("--rows", "How many", DefaultValue = "42")] int rows)
        {
            Seen = rows;
            return 0;
        }
    }

    [Fact]
    public void Apply_A_String_Form_DefaultValue_To_A_Parameter_With_No_Csharp_Default()
    {
        var tool = new DefaultTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        Assert.Equal(0, app.Run("app seed"));
        Assert.Equal(42, tool.Seen);
    }

    // --- Map options: the ?cfg[env]=prod analogue ---------------------------------------------

    public sealed class MapTool
    {
        public Dictionary<string, int>? Seen;

        [CliRoute("reindex")]
        [CliCommandExample("reindex --shard[eu] 3 --shard[us] 5")]
        public int Reindex([CliOption("--shard", "Per-region shard counts")] Dictionary<string, int>? shard = null)
        {
            Seen = shard;
            return 0;
        }
    }

    [Fact]
    public void Bind_A_Map_Option_Into_A_Dictionary()
    {
        var tool = new MapTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        Assert.Equal(0, app.Run("app reindex --shard[eu] 3 --shard[us] 5"));

        Assert.NotNull(tool.Seen);
        Assert.Equal(3, tool.Seen!["eu"]);
        Assert.Equal(5, tool.Seen["us"]);
    }

    // --- CliFlag? (presence-only) vs bool (a two-state VALUE option) --------------------------

    public sealed class FlagTool
    {
        public CliFlag? Flag;
        public bool Bool;

        [CliRoute("flag")]
        [CliCommandExample("flag --dry-run")]
        public int Flagged([CliOption("--dry-run")] CliFlag? dryRun = null)
        {
            Flag = dryRun;
            return 0;
        }

        [CliRoute("bool")]
        [CliCommandExample("bool --force true")]
        public int Booled([CliOption("--force")] bool force = false)
        {
            Bool = force;
            return 0;
        }
    }

    [Fact]
    public void Treat_CliFlag_As_Presence_Only_And_Bool_As_A_Value()
    {
        var tool = new FlagTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        // CliFlag?: the flag is set by being there. No value is read.
        Assert.Equal(0, app.Run("app flag --dry-run"));
        Assert.True(tool.Flag.HasValue);

        Assert.Equal(0, app.Run("app flag"));
        Assert.False(tool.Flag.HasValue);

        // bool: a two-state option that takes a value. This is the distinction users miss.
        Assert.Equal(0, app.Run("app bool --force true"));
        Assert.True(tool.Bool);

        Assert.Equal(0, app.Run("app bool --force false"));
        Assert.False(tool.Bool);
    }

    // --- Human-readable TimeSpan ---------------------------------------------------------------

    public sealed class DurationTool
    {
        public TimeSpan? Seen;

        [CliRoute("drain")]
        [CliCommandExample("drain --timeout \"30 seconds\"")]
        public int Drain([CliOption("--timeout")] TimeSpan? timeout = null)
        {
            Seen = timeout;
            return 0;
        }
    }

    [Theory]
    [InlineData("\"30 seconds\"", 30)]
    [InlineData("\"5 min\"", 300)]
    [InlineData("\"1.5 hours\"", 5400)]
    [InlineData("PT30S", 30)]
    [InlineData("00:00:30", 30)]
    public void Read_A_TimeSpan_The_Way_An_Operator_Would_Type_It(string typed, int expectedSeconds)
    {
        var tool = new DurationTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        Assert.Equal(0, app.Run($"app drain --timeout {typed}"));
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), tool.Seen);
    }

    // --- Sensitive: the value never reaches an echo of the command line -----------------------

    public sealed class SecretTool
    {
        [CliRoute("login")]
        [CliCommandExample("login --password hunter2")]
        public int Login([CliOption("--password", Sensitive = true)] string password) => 0;
    }

    [Fact]
    public void Redact_A_Sensitive_Value_Wherever_The_Framework_Echoes_Argv()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new SecretTool())
            .UseMiddleware(new CliTimingMiddleware()));

        // The timing middleware echoes the whole invocation. The secret must not be in it.
        Assert.Equal(0, app.Run("app login --password hunter2 --timing"));

        var output = console.OutWriter.ToString() + console.ErrorWriter.ToString();
        Assert.DoesNotContain("hunter2", output, StringComparison.Ordinal);
    }

    // --- RankByOptions: a TIE-BREAKER, not overload selection ----------------------------------

    public sealed class RankTool
    {
        public string? Reached;

        // Two routes whose SEGMENT shapes both match `db migrate`: one literal, one placeholder.
        [CliRoute("db migrate")]
        [CliCommandExample("db migrate --force")]
        public int Migrate([CliOption("--force")] CliFlag? force = null)
        {
            Reached = nameof(Migrate);
            return 0;
        }

        [CliRoute("db {command}")]
        [CliCommandExample("db status")]
        public int Passthrough(string command)
        {
            Reached = nameof(Passthrough);
            return 0;
        }
    }

    [Fact]
    public void Break_A_Tie_Between_Equally_Matching_Routes_By_Which_Options_Are_Present()
    {
        var tool = new RankTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        // `--force` is recognized by `db migrate` (+1) and unrecognized by `db {command}` (−1),
        // so the literal route outranks the placeholder and wins.
        Assert.Equal(0, app.Run("app db migrate --force"));
        Assert.Equal(nameof(RankTool.Migrate), tool.Reached);
    }

    [Fact]
    public void Refuse_To_Guess_When_A_Literal_And_A_Placeholder_Route_Tie()
    {
        // WITHOUT a distinguishing option the two routes score identically, and Portico DECLINES to
        // prefer the literal over the placeholder. This is a real constraint, and it is deliberate:
        // specific-route-plus-catch-all is not a supported shape. ASP.NET and Express would silently
        // pick the literal; Portico says so and exits 2.
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg.WithConsole(console).AddCommands(new RankTool()));

        Assert.Equal(2, app.Run("app db migrate"));

        var error = console.ErrorWriter.ToString();
        Assert.Contains("matches more than one command", error, StringComparison.Ordinal);
    }

    // --- "Did you mean" ------------------------------------------------------------------------

    public sealed class TypoTool
    {
        [CliRoute("db migrate")]
        [CliCommandExample("db migrate")]
        public int Migrate() => 0;
    }

    [Fact]
    public void Suggest_The_Route_A_User_Meant_To_Type()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg.WithConsole(console).AddCommands(new TypoTool()));

        Assert.Equal(2, app.Run("app db migrat"));

        var error = console.ErrorWriter.ToString();
        Assert.Contains("db migrate", error, StringComparison.Ordinal);
    }

    // --- Shell completion ----------------------------------------------------------------------

    [Fact]
    public void Emit_A_Shell_Completion_Script()
    {
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(new TypoTool()));

        var bash = new StringWriter();
        app.EmitCompletion(CliCompletionShell.Bash, "admin", bash);

        var script = bash.ToString();
        Assert.Contains("bash completion for admin", script, StringComparison.Ordinal);
        Assert.Contains("db migrate", script, StringComparison.Ordinal);
    }

    // --- CliOptions bundle: the [FromBody] analogue --------------------------------------------

    public sealed class ConnectionOptions : CliOptions
    {
        [CliOption("--host")] public string Host { get; set; } = "localhost";
        [CliOption("--port")] public int Port { get; set; } = 5432;
    }

    public sealed class BundleTool
    {
        public ConnectionOptions? Seen;

        [CliRoute("connect")]
        [CliCommandExample("connect --host db --port 6543")]
        public int Connect(ConnectionOptions connection)
        {
            Seen = connection;
            return 0;
        }
    }

    [Fact]
    public void Bind_A_Group_Of_Options_Into_A_Bundle()
    {
        var tool = new BundleTool();
        var app = CliApplication.Create(cfg => cfg.WithConsole(new StringCliConsole()).AddCommands(tool));

        Assert.Equal(0, app.Run("app connect --host db --port 6543"));

        Assert.Equal("db", tool.Seen!.Host);
        Assert.Equal(6543, tool.Seen.Port);
    }
}

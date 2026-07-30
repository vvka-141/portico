using System;
using System.Linq;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-83 §3. `RouteSignature` interpolates a placeholder's NAME — `x {first}` and `x {second}` are
// distinct strings — so the duplicate-route check at Create does not see them and neither does POR002.
// Both register, and then EVERY invocation of `x anything` exits 2 as ambiguous, because the name is
// invisible at the command line: a user typing `x foo` has no way to indicate which they meant. Two
// commands that can never run is a configuration error, and it now fails at Create.
//
// The narrowness is the design. Same-shape routes distinguished by their OPTIONS genuinely dispatch —
// that is `RankByOptions` doing its documented job, verified by running it — so only the pair no input
// can separate is refused. POR-119's rule: a Create-time throw must never fail a legal composition.
//
// This is NOT POR-114 and does not prejudge it. That ticket is about a *literal* outranking a
// placeholder — two shapes a user CAN tell apart, where the question is which should win, and which
// `capabilities.md` currently documents as a deliberate refusal. Here the shapes are identical after
// normalization and there is nothing to prefer. Keep_Literal_Beside_Placeholder_Buildable pins the
// boundary so this fix cannot quietly grow into that decision.
// ReSharper disable once InconsistentNaming
public sealed class CliRouteIdentity_Should
{
    /// <summary>Same shape, different placeholder names, nothing else to tell them apart.</summary>
    public sealed class IndistinguishableTool
    {
        [CliRoute("x {first}")]
        [CliCommandExample("x a")]
        public int First(string first) => 0;

        [CliRoute("x {second}")]
        [CliCommandExample("x b")]
        public int Second(string second) => 0;
    }

    /// <summary>Same shape, different placeholder names, told apart by their options.</summary>
    public sealed class OptionDistinguishedTool
    {
        public string? Alpha { get; private set; }
        public string? Beta { get; private set; }

        [CliRoute("y {first}")]
        [CliCommandExample("y a --alpha 1")]
        public int First(string first, [CliOption("--alpha")] string alpha = "")
        {
            Alpha = first;
            return 0;
        }

        [CliRoute("y {second}")]
        [CliCommandExample("y b --beta 2")]
        public int Second(string second, [CliOption("--beta")] string beta = "")
        {
            Beta = second;
            return 0;
        }
    }

    /// <summary>A literal beside a catch-all — POR-114's shape, deliberately still buildable.</summary>
    public sealed class LiteralBesidePlaceholderTool
    {
        [CliRoute("db migrate")]
        [CliCommandExample("db migrate")]
        public int Migrate() => 0;

        [CliRoute("db {command}")]
        [CliCommandExample("db vacuum")]
        public int Passthrough(string command) => 0;
    }

    /// <summary>Different shapes entirely — the placeholder is in a different position.</summary>
    public sealed class DifferentShapeTool
    {
        [CliRoute("z {id} details")]
        [CliCommandExample("z 1 details")]
        public int Details(string id) => 0;

        [CliRoute("z list {filter}")]
        [CliCommandExample("z list open")]
        public int List(string filter) => 0;
    }

    // --- The defect: refused at Create rather than dead at every invocation --------------------

    [Fact]
    public void Refuse_Two_Routes_That_Differ_Only_In_A_Placeholder_Name()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new IndistinguishableTool())));

        // Both signatures are named, with their names intact — the user has to see which two routes
        // collided, and the name is the only thing that distinguishes them in source.
        Assert.Contains("x {first}", ex.Message, StringComparison.Ordinal);
        Assert.Contains("x {second}", ex.Message, StringComparison.Ordinal);
        // And what to do about it.
        Assert.Contains("no command line can tell them apart", ex.Message, StringComparison.Ordinal);
        Assert.Contains("different options", ex.Message, StringComparison.Ordinal);
    }

    // Before this landed, the pair built happily and then failed on every input. That is the behaviour
    // being replaced, and it is worth stating what it was: no argument reached either handler, ever.
    [Fact]
    public void Report_At_Create_Rather_Than_On_Every_Invocation()
    {
        // The refusal happens at Create, so there is no application to run — which is the improvement.
        // Nothing here asserts an exit code, because the point is that the exit code never happened.
        var thrown = Record.Exception(
            () => CliApplication.Create(cfg => cfg.AddCommands(new IndistinguishableTool())));

        Assert.IsType<CliConfigurationException>(thrown);
    }

    // --- The narrowness: a legal composition must still build ---------------------------------

    [Fact]
    public void Keep_Same_Shape_Routes_Buildable_When_Their_Options_Separate_Them()
    {
        var tool = new OptionDistinguishedTool();
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(tool));

        harness.Run("app y foo --alpha 1").ExpectExit(0);
        Assert.Equal("foo", tool.Alpha);

        harness.Run("app y bar --beta 2").ExpectExit(0);
        Assert.Equal("bar", tool.Beta);
    }

    // The same pair without a distinguishing option is still ambiguous at run time, and that is
    // correct — the routes are reachable, just not by this command line. Refusing them at Create would
    // have failed a program whose other two invocations work.
    [Fact]
    public void Still_Report_Ambiguity_At_Run_Time_When_No_Option_Was_Supplied()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new OptionDistinguishedTool()))
            .Run("app y foo");

        Assert.Equal(CliExitException.UsageErrorExitCode, result.ExitCode);
        Assert.Contains("matches more than one command", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_Literal_Beside_Placeholder_Buildable()
    {
        // POR-114's shape. `capabilities.md` documents the runtime refusal as deliberate and a test
        // named Refuse_To_Guess_When_A_Literal_And_A_Placeholder_Route_Tie pins it. Whatever that
        // decision becomes, it is not this ticket's to make — so this must keep BUILDING.
        var application = CliApplication.Create(cfg => cfg.AddCommands(new LiteralBesidePlaceholderTool()));

        Assert.NotNull(application);
        Assert.Contains("db migrate", application.GetRouteSignatures());
        Assert.Contains("db {command}", application.GetRouteSignatures());
    }

    [Fact]
    public void Keep_Routes_Whose_Placeholder_Sits_In_A_Different_Position_Buildable()
    {
        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands(new DifferentShapeTool()));

        harness.Run("app z 1 details").ExpectExit(0);
        harness.Run("app z list open").ExpectExit(0);
    }

    // --- The normalization is for identity only ------------------------------------------------

    // RouteSignature is rendered in help, in errors, in shell completion and by the public
    // GetRouteSignatures(), and all of those want the placeholder's name — it is the only clue to what
    // the argument means. Collapsing it for identity must not collapse it anywhere a user reads.
    [Fact]
    public void Keep_The_Placeholder_Name_In_Everything_A_User_Reads()
    {
        var application = CliApplication.Create(cfg => cfg.AddCommands(new DifferentShapeTool()));

        Assert.Equal(
            ["z list {filter}", "z {id} details"],
            application.GetRouteSignatures().OrderBy(s => s, StringComparer.Ordinal));
    }

    // Help renders the placeholder through its display form — `<ID>`, not `{id}` — but it is still the
    // NAME that is rendered, which is the point: collapsing names for identity must not reach the
    // renderer, where the name is the only clue to what the argument means.
    [Fact]
    public void Render_The_Placeholder_Name_In_Help()
    {
        var help = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new DifferentShapeTool()))
            .Run("app --help")
            .StandardOut;

        Assert.Contains("<FILTER>", help, StringComparison.Ordinal);
        Assert.Contains("<ID>", help, StringComparison.Ordinal);
    }

    // --- The pre-existing exact-duplicate check is untouched -----------------------------------

    public sealed class ExactDuplicateTool
    {
        [CliRoute("dup {id}")]
        [CliCommandExample("dup 1")]
        public int One(string id) => 0;

        [CliRoute("dup {id}")]
        [CliCommandExample("dup 2")]
        public int Two(string id) => 0;
    }

    // Identical signatures still hit the older, stricter DuplicateRoute error — which runs first, so the
    // new check never changes the message for a copy-paste duplicate.
    [Fact]
    public void Keep_Reporting_An_Exact_Duplicate_As_A_Duplicate_Route()
    {
        var ex = Assert.Throws<CliConfigurationException>(
            () => CliApplication.Create(cfg => cfg.AddCommands(new ExactDuplicateTool())));

        Assert.DoesNotContain("no command line can tell them apart", ex.Message, StringComparison.Ordinal);
    }
}

using System;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Robustness checks that protect the dispatch pipeline from avoidable NREs and from
// silently-broken programmer configurations.
public sealed class CliApplicationRobustness_Should
{
    public sealed class NoopService
    {
        [CliRoute("noop")]
        [CliCommandExample("noop")]
        public int Noop() => 0;
    }

    // --- null in args -----------------------------------------------------------------------

    [Fact]
    public void Reject_Null_Element_In_Args_Array()
    {
        var app = CliApplication.Create(cfg => cfg.AddCommands(new NoopService()));

        var ex = Assert.Throws<ArgumentException>(() => app.Run(new[] { "noop", null!, "tail" }));

        Assert.Contains("args[1] is null", ex.Message);
    }

    [Fact]
    public void Accept_Empty_String_Element_In_Args_Array_Without_Throwing()
    {
        // Empty string is valid argv content — only null is rejected. Whether a command matches
        // when extra empty segments follow is orthogonal; this test pins the null-check behavior
        // specifically.
        var app = CliApplication.Create(cfg => cfg.AddCommands(new NoopService()));
        // No exception. The exit code isn't asserted — it could be 0 (if empty segments are
        // ignored) or non-zero (usage error) depending on route-matching semantics; both are
        // acceptable outcomes compared to the NRE that used to happen.
        _ = app.Run(new[] { "noop", "", "" });
    }

    // --- Duplicate option aliases ------------------------------------------------------------

    public sealed class DuplicateAliasOnParams
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--verbose|-v")] CliFlag? a,
            [CliOption("--verbose")] CliFlag? b) => 0;
    }

    [Fact]
    public void Reject_Same_Alias_On_Two_Parameters_Of_One_Method()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new DuplicateAliasOnParams())));

        Assert.Contains("--verbose", ex.Message);
        Assert.Contains("declared by both", ex.Message);
    }

    public sealed class BundleWithConflict : CliOptions
    {
        [CliOption("--verbose|-v")]
        public CliFlag? Verbose { get; set; }
    }

    public sealed class ParamAndBundleAliasConflict
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--verbose")] CliFlag? verbose,
            BundleWithConflict bundle) => 0;
    }

    [Fact]
    public void Reject_Alias_Shared_Between_Parameter_And_Bundle_Property()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new ParamAndBundleAliasConflict())));

        Assert.Contains("--verbose", ex.Message);
        Assert.Contains("BundleWithConflict", ex.Message);
    }

    public sealed class ShortFormDuplicate
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(
            [CliOption("--alpha|-x")] CliFlag? a,
            [CliOption("--beta|-x")] CliFlag? b) => 0;
    }

    [Fact]
    public void Reject_Duplicate_Short_Form_Alias_Across_Parameters()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new ShortFormDuplicate())));

        Assert.Contains("-x", ex.Message);
    }

    // --- CliTimingMiddleware -----------------------------------------------------------------

    public sealed class SlowService
    {
        [CliRoute("slow")]
        [CliCommandExample("slow")]
        public int Slow()
        {
            System.Threading.Thread.Sleep(5);
            return 0;
        }
    }

    [Fact]
    public void CliTimingMiddleware_Emit_Line_When_Flag_Present()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .UseMiddleware(new CliTimingMiddleware())
            .AddCommands(new SlowService())
            .WithConsole(console));

        Assert.Equal(0, app.Run("app slow --timing"));
        Assert.Contains("[timing]", console.ErrorWriter.ToString());
        Assert.Contains("slow", console.ErrorWriter.ToString());
    }

    [Fact]
    public void CliTimingMiddleware_Silent_Without_Flag()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .UseMiddleware(new CliTimingMiddleware())
            .AddCommands(new SlowService())
            .WithConsole(console));

        Assert.Equal(0, app.Run("app slow"));
        // No output of any kind (handler doesn't print; middleware is silent without --timing).
        Assert.Empty(console.OutWriter.ToString());
        Assert.Empty(console.ErrorWriter.ToString());
    }
}

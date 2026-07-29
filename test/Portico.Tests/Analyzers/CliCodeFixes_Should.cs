using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliCodeFixes_Should
{
    [Fact]
    public async Task POR004_Insert_CliCommandExample_Stub_That_Compiles()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init")]
    public int Init() => 0;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new MissingCommandExampleAnalyzer(),
            new MissingCommandExampleCodeFix(),
            source);

        Assert.Contains("[CliCommandExample(", fixedSource);
        Assert.Contains("CliRoute", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);
    }

    [Fact]
    public async Task POR006_Insert_Public_Parameterless_Constructor_That_Compiles()
    {
        var source = """
using Portico;

public sealed class MyOptions : CliOptions
{
    public MyOptions(string injected) { }
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new BundleCtorAnalyzer(),
            new BundleMissingCtorCodeFix(),
            source);

        Assert.Contains("public MyOptions()", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);
    }

    [Fact]
    public async Task POR012_Rewrite_A_Bool_Option_To_A_Presence_Only_Flag()
    {
        var source = """
using Portico;

public interface ITool
{
    [CliRoute("run")]
    [CliCommandExample("run")]
    int Run([CliOption("--verbose")] bool verbose = false);
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new BoolUsedAsSwitchAnalyzer(),
            new BoolUsedAsSwitchCodeFix(),
            source);

        Assert.Contains("CliFlag? verbose = null", fixedSource);
        Assert.DoesNotContain("bool verbose", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);

        // The fix must silence the rule it fixes.
        var remaining = await AnalyzerTestRunner.RunAsync(new BoolUsedAsSwitchAnalyzer(), fixedSource);
        Assert.DoesNotContain(remaining, d => d.Id == "POR012");
    }

    [Fact]
    public async Task POR012_Rewrite_A_Bool_Bundle_Property()
    {
        var source = """
using Portico;

public sealed class Options : CliOptions
{
    [CliOption("--force")] public bool Force { get; set; } = false;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new BoolUsedAsSwitchAnalyzer(),
            new BoolUsedAsSwitchCodeFix(),
            source);

        Assert.Contains("CliFlag? Force", fixedSource);
        // A `= false` initializer would not compile against CliFlag?.
        Assert.DoesNotContain("= false", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);
    }

    [Theory]
    // The three catch-all shapes, including the bare `catch` that has no identifier to name in a
    // filter — the fix writes the declaration too, which is a formatting change rather than a
    // semantic one, since `catch` and `catch (Exception)` are the same clause to the compiler.
    [InlineData("catch { return 0; }")]
    [InlineData("catch (Exception) { return 0; }")]
    // Console is not in the code-fix harness's reference set, so the body uses the caught
    // exception without printing it — the point is that an existing identifier is reused, not
    // shadowed by a second one.
    [InlineData("catch (Exception ex) { return ex.HResult; }")]
    public async Task POR013_Add_The_Filter_That_Lets_A_Controlled_Exit_Through(string catchClause)
    {
        var source = $$"""
using System;
using Portico;

public sealed class Svc
{
    [CliRoute("run")]
    [CliCommandExample("run")]
    public int Run()
    {
        try { throw new CliExitException("boom") { ExitCode = 17 }; }
        {{catchClause}}
    }
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new SwallowedCliExitExceptionAnalyzer(),
            new SwallowedCliExitExceptionCodeFix(),
            source);

        Assert.Contains("when (ex is not CliExitException)", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);

        // The fix has to actually silence the rule it fixes. A filter that compiles but still
        // reports would be worse than no fix at all.
        var remaining = await AnalyzerTestRunner.RunAsync(
            new SwallowedCliExitExceptionAnalyzer(), fixedSource);
        Assert.DoesNotContain(remaining, d => d.Id == "POR013");
    }
}

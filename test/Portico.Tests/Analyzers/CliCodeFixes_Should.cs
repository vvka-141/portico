using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
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

    // =============================================================================================
    //  POR-122 — fixes for POR005, POR001 and POR003.
    //
    //  Eight of Portico's twelve rules stopped the build and offered prose only, against a pitch of
    //  "compile-time checks, not runtime surprises". A rule with a fix reads as helpful; a rule
    //  without one reads as strict. These three are the ones whose correction is deterministic.
    //
    //  Each fix is verified the same way: apply it, then re-run the analyzer and assert the
    //  diagnostic is GONE. Asserting on the fixed text alone would pass on a fix that produced
    //  something the analyzer still rejects — which is the failure a code fix can most easily have.
    // =============================================================================================

    // --- POR005: [CliArgument] with no placeholder -> append it to the route --------------------

    [Fact]
    public async Task POR005_Add_The_Missing_Placeholder_To_The_Route()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("user details")]
    [CliCommandExample("user details")]
    public int Details([CliArgument("which user")] string id) => 0;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new CliArgumentParameterAnalyzer(),
            new CliArgumentRouteCodeFix(),
            source);

        Assert.Contains("""[CliRoute("user details {id}")]""", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);

        var remaining = await AnalyzerTestRunner.RunAsync(new CliArgumentParameterAnalyzer(), fixedSource);
        Assert.DoesNotContain(remaining, d => d.Id == "POR005");
    }

    // A route that is only a placeholder-less single segment still gets a correct result — the append
    // is `Trim()`ed, so no double space appears.
    [Fact]
    public async Task POR005_Append_Cleanly_To_A_Single_Segment_Route()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("greet")]
    [CliCommandExample("greet")]
    public int Greet([CliArgument("who")] string name) => 0;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new CliArgumentParameterAnalyzer(),
            new CliArgumentRouteCodeFix(),
            source);

        Assert.Contains("""[CliRoute("greet {name}")]""", fixedSource);
        Assert.DoesNotContain("greet  {name}", fixedSource);
    }

    // --- POR001: placeholder with no parameter -> rename, or add the parameter ------------------

    [Fact]
    public async Task POR001_Offer_A_Rename_Per_Parameter_And_An_Add()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("user {userId} details")]
    [CliCommandExample("user 42 details")]
    public int Details(string id) => 0;
}
""";

        var titles = await CodeFixTestRunner.OfferedTitlesAsync(
            new RoutePlaceholderAnalyzer(),
            new RoutePlaceholderCodeFix(),
            source);

        // One rename per available parameter, plus the add. Neither remedy is presumed — POR001's own
        // message names both, and which is right is the author's call.
        Assert.Equal(2, titles.Count);
        Assert.Contains("Rename placeholder '{userId}' to '{id}'", titles);
        Assert.Contains("Add parameter 'string userId'", titles);
    }

    [Fact]
    public async Task POR001_Rename_The_Placeholder_To_An_Existing_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("user {userId} details")]
    [CliCommandExample("user 42 details")]
    public int Details(string id) => 0;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new RoutePlaceholderAnalyzer(),
            new RoutePlaceholderCodeFix(),
            source,
            select: title => title.Contains("Rename"));

        Assert.Contains("""[CliRoute("user {id} details")]""", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);

        var remaining = await AnalyzerTestRunner.RunAsync(new RoutePlaceholderAnalyzer(), fixedSource);
        Assert.DoesNotContain(remaining, d => d.Id == "POR001");
    }

    [Fact]
    public async Task POR001_Add_The_Parameter_The_Placeholder_Names()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("user {userId} details")]
    [CliCommandExample("user 42 details")]
    public int Details(string id) => 0;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new RoutePlaceholderAnalyzer(),
            new RoutePlaceholderCodeFix(),
            source,
            select: title => title.Contains("Add parameter"));

        Assert.Contains("string userId", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);

        var remaining = await AnalyzerTestRunner.RunAsync(new RoutePlaceholderAnalyzer(), fixedSource);
        Assert.DoesNotContain(remaining, d => d.Id == "POR001");
    }

    // A method with no parameters has nothing to rename to, so only the add is offered. Registering a
    // rename with no target would be an action that cannot do anything.
    [Fact]
    public async Task POR001_Offer_Only_The_Add_When_The_Method_Takes_No_Parameters()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("user {userId}")]
    [CliCommandExample("user 42")]
    public int Details() => 0;
}
""";

        var titles = await CodeFixTestRunner.OfferedTitlesAsync(
            new RoutePlaceholderAnalyzer(),
            new RoutePlaceholderCodeFix(),
            source);

        Assert.Single(titles);
        Assert.Contains("Add parameter 'string userId'", titles);
    }

    // The rename is token-wise, not a substring replace. `user{userId}` is a LITERAL segment the
    // runtime routes fine (POR-61), so rewriting inside it would break a working route. Here the bad
    // placeholder is a whole token and the literal lookalike must survive untouched.
    [Fact]
    public async Task POR001_Leave_A_Braced_Literal_Segment_Alone_When_Renaming()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("user{userId} {userId} details")]
    [CliCommandExample("user{userId} 42 details")]
    public int Details(string id) => 0;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new RoutePlaceholderAnalyzer(),
            new RoutePlaceholderCodeFix(),
            source,
            select: title => title.Contains("Rename"));

        Assert.Contains("""[CliRoute("user{userId} {id} details")]""", fixedSource);
    }

    // --- POR003: malformed [CliOption] spec -> only the deterministic subset --------------------

    [Theory]
    [InlineData("\"verbose\"", "\"--verbose\"")]        // undashed long name
    [InlineData("\"v\"", "\"-v\"")]                      // undashed single char -> short form
    [InlineData("\"--verbose|\"", "\"--verbose\"")]      // trailing pipe
    [InlineData("\"|--verbose\"", "\"--verbose\"")]      // leading pipe
    [InlineData("\"--verbose||-v\"", "\"--verbose|-v\"")] // doubled pipe
    [InlineData("\"verbose|v\"", "\"--verbose|-v\"")]    // both aliases undashed
    public async Task POR003_Repair_The_Specs_That_Carry_Author_Intent(string declared, string expected)
    {
        var source = $$"""
using Portico;

public class Svc
{
    [CliRoute("run")]
    [CliCommandExample("run")]
    public int Run([CliOption({{declared}})] string value = "") => 0;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new CliOptionSpecAnalyzer(),
            new CliOptionSpecCodeFix(),
            source);

        Assert.Contains($"[CliOption({expected})]", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);

        // The repair is re-validated before it is offered, so the rule must be silent afterwards. This
        // is the assertion that makes the whole fix trustworthy rather than merely plausible.
        var remaining = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), fixedSource);
        Assert.DoesNotContain(remaining, d => d.Id == "POR003");
    }

    // The negative case, and the reason this fix is narrow. None of these carries a recoverable name,
    // so NO action is registered — the error stands with its message. "A code fix that guesses is worse
    // than no code fix, because the user accepts it without reading."
    [Theory]
    [InlineData("\"\"")]        // empty
    [InlineData("\"   \"")]     // whitespace only
    [InlineData("\"-\"")]       // a dash with no name
    [InlineData("\"--\"")]      // the POSIX terminator
    [InlineData("\" --verbose\"")] // padded alias: reshaping layout is not a correction
    public async Task POR003_Offer_Nothing_When_There_Is_No_Intent_To_Recover(string declared)
    {
        var source = $$"""
using Portico;

public class Svc
{
    [CliRoute("run")]
    [CliCommandExample("run")]
    public int Run([CliOption({{declared}})] string value = "") => 0;
}
""";

        var titles = await CodeFixTestRunner.OfferedTitlesAsync(
            new CliOptionSpecAnalyzer(),
            new CliOptionSpecCodeFix(),
            source);

        Assert.Empty(titles);
    }

    // --- Fix-all keys ---------------------------------------------------------------------------

    // A parameter rename produces many POR001s at once, and fixing them one at a time is the failure
    // mode POR-122 exists to remove. Every provider must therefore return a Fix-all provider — the
    // detail the ticket calls most likely to be skipped.
    [Fact]
    public void Support_Fix_All_On_Every_Provider()
    {
        CodeFixProvider[] providers =
        [
            new CliArgumentRouteCodeFix(),
            new RoutePlaceholderCodeFix(),
            new CliOptionSpecCodeFix(),
            new MissingCommandExampleCodeFix(),
            new BundleMissingCtorCodeFix(),
            new BoolUsedAsSwitchCodeFix(),
            new SwallowedCliExitExceptionCodeFix(),
        ];

        Assert.All(providers, provider => Assert.NotNull(provider.GetFixAllProvider()));
    }
}

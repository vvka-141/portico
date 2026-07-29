using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Portico.Analyzers;

// POR012. `[CliOption("--verbose")] bool verbose` compiles, runs, and produces a CLI where
// `--verbose` alone does not work — a user has to type `--verbose true`. The framework's own
// reference calls this its most common misuse, which is the definition of a pit of failure.
//
// The operator decision of 2026-07-24 was to keep CliFlag? rather than reinterpret bool as a switch:
// `--flag=false` has no coherent meaning, and a switch often implies a different set of legal
// options, which a value cannot model. Given that, the answer is a diagnostic at the edge.
public sealed class BoolUsedAsSwitchAnalyzer_Should
{
    private static async Task<int> Por012Count(string source) =>
        (await AnalyzerTestRunner.RunAsync(new BoolUsedAsSwitchAnalyzer(), source))
            .Count(d => d.Id == "POR012");

    private static string Contract(string parameter) => $$"""
        using System.Collections.Generic;
        using Portico;

        public interface ITool
        {
            [CliRoute("run")]
            [CliCommandExample("run")]
            int Run({{parameter}});
        }
        """;

    [Theory]
    [InlineData("[CliOption(\"--verbose\")] bool verbose = false")]
    [InlineData("[CliOption(\"--verbose|-v\")] bool verbose = false")]
    [InlineData("[CliOption(\"--verbose\")] bool? verbose = null")]
    public async Task Flag_A_Bool_Option(string parameter)
    {
        Assert.Equal(1, await Por012Count(Contract(parameter)));
    }

    [Theory]
    [InlineData("[CliOption(\"--verbose\")] CliFlag? verbose = null")]
    [InlineData("[CliOption(\"--name\")] string name = \"\"")]
    [InlineData("[CliOption(\"--rows\")] int rows = 0")]
    [InlineData("[CliOption(\"--flags\")] List<bool>? flags = null")]
    public async Task Stay_Silent_On_Everything_Else(string parameter)
    {
        Assert.Equal(0, await Por012Count(Contract(parameter)));
    }

    /// <summary>
    /// A bare <c>bool</c> with no <c>[CliOption]</c> is not an option at all — the rule is about the
    /// declaration, not about the type.
    /// </summary>
    [Fact]
    public async Task Ignore_A_Bool_That_Is_Not_An_Option()
    {
        const string source = """
            using Portico;

            public sealed class NotAContract
            {
                public int Run(bool verbose) => 0;
            }
            """;

        Assert.Equal(0, await Por012Count(source));
    }

    /// <summary>
    /// The bundle path. <c>CliOptionParameterInfo</c> and <c>CliOptionsPropertyInfo</c> have drifted
    /// before (POR-59), and the mistake is identical on a bundle property.
    /// </summary>
    [Fact]
    public async Task Flag_A_Bool_Bundle_Property()
    {
        const string source = """
            using Portico;

            public sealed class Options : CliOptions
            {
                [CliOption("--force")] public bool Force { get; set; }
            }
            """;

        Assert.Equal(1, await Por012Count(source));
    }

    /// <summary>
    /// The message has to name the option the way the user types it and the replacement they want,
    /// because a diagnostic that only says "this is wrong" is the failure mode POR-49 audited for.
    /// </summary>
    [Fact]
    public async Task Name_The_Option_And_The_Replacement()
    {
        var diagnostic = Assert.Single(
            await AnalyzerTestRunner.RunAsync(
                new BoolUsedAsSwitchAnalyzer(),
                Contract("[CliOption(\"--dry-run|-d\")] bool dryRun = false")));

        var message = diagnostic.GetMessage();
        Assert.Contains("--dry-run true", message);          // the first alias, as typed
        Assert.Contains("CliFlag? dryRun = null", message);  // the replacement, with the real name
        Assert.Contains("safe to suppress", message);        // the escape hatch, in the message itself
    }

    /// <summary>
    /// Warning, not Error, and pinned so the decision cannot be changed by accident.
    /// </summary>
    /// <remarks>
    /// <c>bool</c> is legitimate for a genuine two-state option and this rule cannot tell that case
    /// from the mistake, so it may not fail a build on its own authority. Note what that means in
    /// practice: this repository and the <c>portico-cli</c> template both set
    /// <c>TreatWarningsAsErrors</c>, so for a scaffolded project it <em>is</em> a build failure —
    /// accepted deliberately, because <c>Info</c> is invisible in <c>dotnet build</c> and in CI,
    /// which is exactly where this mistake ships from.
    /// <para>
    /// Proven by the two suppressions in <c>examples/ReferenceCli</c>: the rule fired on the
    /// example's deliberate two-state options the first time it was built, which is what the escape
    /// hatch is for and what the message advertises.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Report_At_Warning_Severity()
    {
        var diagnostic = Assert.Single(
            await AnalyzerTestRunner.RunAsync(
                new BoolUsedAsSwitchAnalyzer(),
                Contract("[CliOption(\"--verbose\")] bool verbose = false")));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }
}

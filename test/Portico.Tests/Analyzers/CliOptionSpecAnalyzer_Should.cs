using System.Linq;
using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico.Analyzers;

// ReSharper disable once InconsistentNaming
public sealed class CliOptionSpecAnalyzer_Should
{
    [Fact]
    public async Task Accept_Valid_Long_Form()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run([CliOption("--verbose")] CliFlag? v = null) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR003"));
    }

    [Fact]
    public async Task Accept_Valid_Long_And_Short_Pipe_Separated()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run([CliOption("--verbose|-v")] CliFlag? v = null) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR003"));
    }

    [Fact]
    public async Task Flag_Empty_Spec()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run([CliOption("")] CliFlag? v = null) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), source);
        Assert.Single(diags.Where(d => d.Id == "POR003"));
    }

    [Fact]
    public async Task Flag_Whitespace_Only_Spec()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run([CliOption("   ")] CliFlag? v = null) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), source);
        Assert.Single(diags.Where(d => d.Id == "POR003"));
    }

    [Fact]
    public async Task Flag_Missing_Leading_Dash()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run([CliOption("verbose")] CliFlag? v = null) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), source);
        var d = diags.Single(x => x.Id == "POR003");
        Assert.Contains("missing a leading '-'", d.GetMessage());
    }

    [Fact]
    public async Task Flag_Double_Dash_Only()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run([CliOption("--")] CliFlag? v = null) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), source);
        var d = diags.Single(x => x.Id == "POR003");
        Assert.Contains("reserved", d.GetMessage());
    }

    [Fact]
    public async Task Flag_Trailing_Pipe()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run([CliOption("--verbose|")] CliFlag? v = null) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), source);
        var d = diags.Single(x => x.Id == "POR003");
        Assert.Contains("empty alias", d.GetMessage());
    }

    [Fact]
    public async Task Flag_Single_Dash()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run([CliOption("-")] CliFlag? v = null) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliOptionSpecAnalyzer(), source);
        var d = diags.Single(x => x.Id == "POR003");
        Assert.Contains("just a dash", d.GetMessage());
    }
}

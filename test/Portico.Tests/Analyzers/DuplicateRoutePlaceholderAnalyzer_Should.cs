using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Portico.Analyzers;

// POR011. A {placeholder} repeated in a [CliRoute] string silently discards a value at dispatch.
// The analyzer catches it at build time; the runtime guard at CliApplication.Create is the backstop.
public sealed class DuplicateRoutePlaceholderAnalyzer_Should
{
    private static async Task<int> Por011Count(string source) =>
        (await AnalyzerTestRunner.RunAsync(new DuplicateRoutePlaceholderAnalyzer(), source))
            .Count(d => d.Id == "POR011");

    [Fact]
    public async Task Flag_A_Repeated_Placeholder()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("copy {p} {p}")]
                [CliCommandExample("copy a b")]
                public int Copy(string p) => 0;
            }
            """;

        Assert.Equal(1, await Por011Count(source));
    }

    [Fact]
    public async Task Name_The_Route_And_Placeholder()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("copy {p} {p}")]
                [CliCommandExample("copy a b")]
                public int Copy(string p) => 0;
            }
            """;

        var diagnostic = Assert.Single(
            await AnalyzerTestRunner.RunAsync(new DuplicateRoutePlaceholderAnalyzer(), source));
        var message = diagnostic.GetMessage();

        Assert.Contains("copy {p} {p}", message);
        Assert.Contains("{p}", message);
        Assert.Contains("Copy", message);
    }

    [Fact]
    public async Task Accept_Distinct_Placeholders()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("move {src} {dst}")]
                [CliCommandExample("move a b")]
                public int Move(string src, string dst) => 0;
            }
            """;

        Assert.Equal(0, await Por011Count(source));
    }

    [Fact]
    public async Task Ignore_A_Method_Without_CliRoute()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                public int NotARoute(string p) => 0;
            }
            """;

        Assert.Equal(0, await Por011Count(source));
    }
}

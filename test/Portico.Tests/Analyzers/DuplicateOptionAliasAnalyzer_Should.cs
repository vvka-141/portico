using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Portico.Analyzers;

// ReSharper disable once InconsistentNaming
//
// POR009. The framework already rejects this at CliApplication.Create — the analyzer moves the
// failure into the edit loop, where an agent (or a person) can act on it without running anything.
public sealed class DuplicateOptionAliasAnalyzer_Should
{
    private static async Task<int> Por009Count(string source) =>
        (await AnalyzerTestRunner.RunAsync(new DuplicateOptionAliasAnalyzer(), source))
            .Count(d => d.Id == "POR009");

    [Fact]
    public async Task Flag_Two_Parameters_Sharing_An_Alias()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("a")]
                [CliCommandExample("a --name x")]
                public int A(
                    [CliOption("--name")] string first,
                    [CliOption("--name")] string second) => 0;
            }
            """;

        Assert.Equal(1, await Por009Count(source));
    }

    [Fact]
    public async Task Name_Both_Declarations_And_The_Alias()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("a")]
                [CliCommandExample("a --name x")]
                public int A(
                    [CliOption("--name")] string first,
                    [CliOption("--name")] string second) => 0;
            }
            """;

        var diagnostic = Assert.Single(
            await AnalyzerTestRunner.RunAsync(new DuplicateOptionAliasAnalyzer(), source));
        var message = diagnostic.GetMessage();

        Assert.Contains("'--name'", message);
        Assert.Contains("'first'", message);
        Assert.Contains("'second'", message);
    }

    [Fact]
    public async Task Flag_A_Collision_Inside_A_Pipe_Separated_Spec()
    {
        // Only the short form collides. A spec is an alias LIST, not one name.
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("a")]
                [CliCommandExample("a --name x")]
                public int A(
                    [CliOption("--name|-n")] string first,
                    [CliOption("--nickname|-n")] string second) => 0;
            }
            """;

        Assert.Equal(1, await Por009Count(source));
    }

    [Fact]
    public async Task Flag_A_Bundle_Property_Colliding_With_A_Parameter()
    {
        // The sneaky one: the two declarations live in different types, so nothing on screen shows
        // the collision. The runtime catches it; now so does the build.
        const string source = """
            using Portico;

            public sealed class Bundle : CliOptions
            {
                [CliOption("--name")] public string Name { get; set; } = "";
            }

            public sealed class S
            {
                [CliRoute("a")]
                [CliCommandExample("a --name x")]
                public int A([CliOption("--name")] string first, Bundle bundle) => 0;
            }
            """;

        Assert.Equal(1, await Por009Count(source));
    }

    [Fact]
    public async Task Accept_Distinct_Aliases()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("a")]
                [CliCommandExample("a --name x --age 3")]
                public int A(
                    [CliOption("--name|-n")] string name,
                    [CliOption("--age|-a")] int age) => 0;
            }
            """;

        Assert.Equal(0, await Por009Count(source));
    }

    [Fact]
    public async Task Treat_Short_Aliases_As_Case_Sensitive()
    {
        // -v and -V are DIFFERENT options: curl -v (verbose) vs curl -V (version). Flagging this
        // would make a near-universal CLI idiom inexpressible. The rule mirrors CliAliasComparer.
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("a")]
                [CliCommandExample("a -v")]
                public int A(
                    [CliOption("-v")] string verbose,
                    [CliOption("-V")] string version) => 0;
            }
            """;

        Assert.Equal(0, await Por009Count(source));
    }

    [Fact]
    public async Task Treat_Long_Aliases_As_Case_Insensitive()
    {
        // ...but --name and --NAME are the same option, and binding both is the silent double-bind
        // the framework rejects.
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("a")]
                [CliCommandExample("a --name x")]
                public int A(
                    [CliOption("--name")] string first,
                    [CliOption("--NAME")] string second) => 0;
            }
            """;

        Assert.Equal(1, await Por009Count(source));
    }

    [Fact]
    public async Task Ignore_A_Method_That_Is_Not_A_Route()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                public int NotARoute(
                    [CliOption("--name")] string first,
                    [CliOption("--name")] string second) => 0;
            }
            """;

        Assert.Equal(0, await Por009Count(source));
    }

    [Fact]
    public async Task Ignore_The_Same_Alias_On_Two_Different_Commands()
    {
        // Aliases are scoped to a command. Two commands both taking --name is ordinary.
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("a")]
                [CliCommandExample("a --name x")]
                public int A([CliOption("--name")] string name) => 0;

                [CliRoute("b")]
                [CliCommandExample("b --name x")]
                public int B([CliOption("--name")] string name) => 0;
            }
            """;

        Assert.Equal(0, await Por009Count(source));
    }
}

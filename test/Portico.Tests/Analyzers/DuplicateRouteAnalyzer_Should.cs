using System.Linq;
using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico.Analyzers;

// ReSharper disable once InconsistentNaming
public sealed class DuplicateRouteAnalyzer_Should
{
    [Fact]
    public async Task Accept_Unique_Routes()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("init")]
                [CliCommandExample("init")]
                public int Init() => 0;

                [CliRoute("deploy")]
                [CliCommandExample("deploy")]
                public int Deploy() => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateRouteAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR002"));
    }

    [Fact]
    public async Task Flag_Exact_Duplicate_Across_Methods_In_Same_Class()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("init")]
                [CliCommandExample("init")]
                public int InitA() => 0;

                [CliRoute("init")]
                [CliCommandExample("init")]
                public int InitB() => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateRouteAnalyzer(), source);
        var d = diags.Single(x => x.Id == "POR002");
        Assert.Contains("'init'", d.GetMessage());
        Assert.Contains("InitA", d.GetMessage());
        Assert.Contains("InitB", d.GetMessage());
    }

    [Fact]
    public async Task Say_Nothing_About_The_Same_Route_On_Two_Different_Contracts()
    {
        // POR-52. This test used to assert the OPPOSITE, and it was encoding a false positive: the
        // analyzer was stricter than the framework it guards.
        //
        // Two contracts each declaring `status` are a legal, working program. They may be mounted
        // under different root routes — the runtime dedups on the signature AFTER the mount is
        // prepended, so `storage status` and `queue status` never collide (examples/PlatformCli ships
        // exactly this) — or the application may simply register only one of them. The analyzer can
        // see neither the mount nor the registration.
        //
        // An analyzer must never fail a build that works. The genuinely ambiguous case is still
        // rejected at CliApplication.Create, where the registrations are visible.
        const string source = """
            using Portico;

            public sealed class A
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run() => 0;
            }

            public sealed class B
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run() => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateRouteAnalyzer(), source);
        Assert.Empty(diags.Where(x => x.Id == "POR002"));
    }

    [Fact]
    public async Task Still_Flag_A_Duplicate_Inside_One_Contract()
    {
        // One type is one command surface. A route repeated there is unambiguously a mistake — there
        // is no mount, and no registration choice, that could make it legal.
        const string source = """
            using Portico;

            public sealed class A
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int RunOnce() => 0;

                [CliRoute("run")]
                [CliCommandExample("run")]
                public int RunTwice() => 0;
            }

            public sealed class B
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run() => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateRouteAnalyzer(), source);

        // Exactly one: A's internal duplicate. B's identical route is not the analyzer's business.
        var diagnostic = Assert.Single(diags.Where(x => x.Id == "POR002"));
        Assert.Contains("RunTwice", diagnostic.GetMessage());
        Assert.DoesNotContain("class B", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Flag_N_Minus_One_Duplicates_For_N_Colliding_Routes()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int RunA() => 0;

                [CliRoute("run")]
                [CliCommandExample("run")]
                public int RunB() => 0;

                [CliRoute("run")]
                [CliCommandExample("run")]
                public int RunC() => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateRouteAnalyzer(), source);
        // Three colliding methods → report two duplicates (the second and third, each pointing to the first).
        Assert.Equal(2, diags.Count(x => x.Id == "POR002"));
    }

    [Fact]
    public async Task Treat_Whitespace_Variations_As_Equivalent()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("db migrate")]
                [CliCommandExample("db migrate")]
                public int A() => 0;

                [CliRoute("db  migrate")]
                [CliCommandExample("db migrate")]
                public int B() => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateRouteAnalyzer(), source);
        Assert.Single(diags.Where(x => x.Id == "POR002"));
    }

    [Fact]
    public async Task Flag_Case_Only_Duplicate_Matching_The_Runtime()
    {
        // The runtime dedups routes with StringComparer.OrdinalIgnoreCase, so "Commit" and "commit"
        // throw CliConfigurationException at Create time. POR002 must surface the same collision.
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("Commit")]
                [CliCommandExample("Commit")]
                public int A() => 0;

                [CliRoute("commit")]
                [CliCommandExample("commit")]
                public int B() => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateRouteAnalyzer(), source);
        Assert.Single(diags.Where(x => x.Id == "POR002"));
    }

    [Fact]
    public async Task Distinguish_Routes_With_Different_Placeholder_Names()
    {
        // {a} and {b} produce different canonical signatures — not flagged here.
        // (The ambiguous-match path at dispatch time is a different concern.)
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("x {a}")]
                [CliCommandExample("x foo")]
                public int A(string a) => 0;

                [CliRoute("x {b}")]
                [CliCommandExample("x bar")]
                public int B(string b) => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateRouteAnalyzer(), source);
        Assert.Empty(diags.Where(x => x.Id == "POR002"));
    }
}

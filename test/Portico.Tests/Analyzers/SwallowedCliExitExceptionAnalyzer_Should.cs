using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Portico.Analyzers;

// POR013. A catch-all in a handler swallows CliExitException, so a failed command can exit 0 — the
// one failure a CI step, a Kubernetes job or a deployment gate cannot see, because they read the
// exit code and nothing else.
//
// Reproduced against the framework before the rule was written: a handler that catches its own
// `throw new CliExitException("fatal: disk full") { ExitCode = 17 }` with `catch (Exception)`
// returns 0, while the same handler with `when (ex is not CliExitException)` or a bare `throw;`
// returns 17.
public sealed class SwallowedCliExitExceptionAnalyzer_Should
{
    private static async Task<int> Por013Count(string source) =>
        (await AnalyzerTestRunner.RunAsync(new SwallowedCliExitExceptionAnalyzer(), source))
            .Count(d => d.Id == "POR013");

    /// <summary>A handler on a plain class — the `AddCommands(new Tool())` registration path.</summary>
    private static string Handler(string catchClause) => $$"""
        using System;
        using System.IO;
        using Portico;

        public sealed class S
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

    [Theory]
    [InlineData("catch { return 0; }")]
    [InlineData("catch (Exception) { return 0; }")]
    [InlineData("catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }")]
    [InlineData("catch (CliExitException) { return 0; }")]
    public async Task Flag_A_Clause_That_Swallows_The_Exit(string catchClause)
    {
        Assert.Equal(1, await Por013Count(Handler(catchClause)));
    }

    [Theory]
    [InlineData("catch (Exception ex) when (ex is not CliExitException) { return 1; }")]
    [InlineData("catch (Exception) { throw; }")]
    [InlineData("catch (IOException) { return 1; }")]
    [InlineData("catch (CliExitException) { throw; }")]
    public async Task Stay_Silent_When_The_Exit_Still_Reaches_The_Boundary(string catchClause)
    {
        Assert.Equal(0, await Por013Count(Handler(catchClause)));
    }

    /// <summary>
    /// The rule is about handlers, not about catch clauses. An ordinary method is none of its
    /// business — reporting there would make it noise in every file of a consuming project.
    /// </summary>
    [Fact]
    public async Task Ignore_A_Method_That_Is_Not_A_Handler()
    {
        const string source = """
            using System;
            using Portico;

            public sealed class S
            {
                public int Helper()
                {
                    try { throw new CliExitException("boom"); }
                    catch (Exception) { return 0; }
                }
            }
            """;

        Assert.Equal(0, await Por013Count(source));
    }

    /// <summary>
    /// The contract-first shape, and the reason this rule needed interface resolution at all:
    /// <c>[CliRoute]</c> is on the interface — that is the whole DispatchProxy design — while the
    /// body containing the <c>catch</c> is on the implementing class.
    /// </summary>
    [Fact]
    public async Task Resolve_The_Route_Through_An_Implemented_Interface()
    {
        const string source = """
            using System;
            using Portico;

            public interface ITool
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                int Run();
            }

            public sealed class Tool : ITool
            {
                public int Run()
                {
                    try { throw new CliExitException("boom"); }
                    catch (Exception) { return 0; }
                }
            }
            """;

        Assert.Equal(1, await Por013Count(source));
    }

    /// <summary>An explicit implementation names its interface member directly — a different symbol shape.</summary>
    [Fact]
    public async Task Resolve_An_Explicit_Interface_Implementation()
    {
        const string source = """
            using System;
            using Portico;

            public interface ITool
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                int Run();
            }

            public sealed class Tool : ITool
            {
                int ITool.Run()
                {
                    try { throw new CliExitException("boom"); }
                    catch (Exception) { return 0; }
                }
            }
            """;

        Assert.Equal(1, await Por013Count(source));
    }

    /// <summary>
    /// A class may implement several contracts and only one of them declare the route. The method
    /// that is a handler is reported; the method that is not, is not.
    /// </summary>
    [Fact]
    public async Task Report_Only_The_Method_Whose_Interface_Declares_The_Route()
    {
        const string source = """
            using System;
            using Portico;

            public interface IRouted
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                int Run();
            }

            public interface IPlain
            {
                int Helper();
            }

            public sealed class Tool : IRouted, IPlain
            {
                public int Run()
                {
                    try { throw new CliExitException("boom"); }
                    catch (Exception) { return 0; }
                }

                public int Helper()
                {
                    try { throw new CliExitException("boom"); }
                    catch (Exception) { return 0; }
                }
            }
            """;

        var diagnostics = await AnalyzerTestRunner.RunAsync(new SwallowedCliExitExceptionAnalyzer(), source);
        var por013 = diagnostics.Where(d => d.Id == "POR013").ToArray();

        var single = Assert.Single(por013);
        Assert.Contains("Run", single.GetMessage());
        Assert.DoesNotContain("Helper", single.GetMessage());
    }

    /// <summary>
    /// A type-level <c>[CliRoute]</c> is a route <em>prefix</em>, not a command declaration —
    /// verified by running such a method through <c>CliApplication</c>, which reports "Unknown
    /// command". Treating it as a handler would report on every method of a prefixed class.
    /// </summary>
    [Fact]
    public async Task Not_Treat_A_Type_Level_Route_Prefix_As_A_Handler()
    {
        const string source = """
            using System;
            using Portico;

            [CliRoute("db")]
            public sealed class S
            {
                public int NotACommand()
                {
                    try { throw new CliExitException("boom"); }
                    catch (Exception) { return 0; }
                }
            }
            """;

        Assert.Equal(0, await Por013Count(source));
    }

    /// <summary>
    /// A bare <c>throw;</c> inside a <em>nested</em> catch re-raises that inner exception, not the
    /// one this clause caught. The outer clause still swallows and is still reported.
    /// </summary>
    [Fact]
    public async Task Not_Count_A_Rethrow_From_A_Nested_Catch()
    {
        const string source = """
            using System;
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run()
                {
                    try { throw new CliExitException("boom"); }
                    catch (Exception)
                    {
                        try { Console.WriteLine("cleanup"); }
                        catch (Exception) { throw; }
                        return 0;
                    }
                }
            }
            """;

        Assert.Equal(1, await Por013Count(source));
    }

    [Fact]
    public async Task Name_The_Clause_And_The_Handler()
    {
        var diagnostic = Assert.Single(
            await AnalyzerTestRunner.RunAsync(
                new SwallowedCliExitExceptionAnalyzer(),
                Handler("catch (Exception) { return 0; }")));

        var message = diagnostic.GetMessage();
        Assert.Contains("catch (Exception)", message);
        Assert.Contains("Run", message);
        Assert.Contains("exit 0", message);
    }

    /// <summary>
    /// Warning, not Error. A catch-all is legal C# with defensible uses and does not break a
    /// framework guarantee the way a route with no example does (POR004, which is Error for exactly
    /// that reason). Pinned so the severity is a decision rather than an accident.
    /// </summary>
    [Fact]
    public async Task Report_At_Warning_Severity()
    {
        var diagnostic = Assert.Single(
            await AnalyzerTestRunner.RunAsync(
                new SwallowedCliExitExceptionAnalyzer(),
                Handler("catch (Exception) { return 0; }")));

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, diagnostic.Severity);
    }
}

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Portico.Analyzers;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliArgumentAnalyzers_Should
{
    // ── POR005: [CliArgument] has no matching route placeholder ────────────

    [Fact]
    public async Task POR005_Report_When_An_Argument_Has_No_Placeholder()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("cp {dest}")]
    [CliCommandExample("cp a b")]
    public int Copy([CliArgument("source path")] string src, string dest) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new CliArgumentParameterAnalyzer(), source);

        var por005 = diags.Where(d => d.Id == "POR005").ToArray();
        Assert.Single(por005);
        Assert.Contains("src", por005[0].GetMessage());
        Assert.Contains("Copy", por005[0].GetMessage());
        // The diagnostic must hand back the corrected route, not merely diagnose.
        Assert.Contains("""[CliRoute("cp {dest} {src}")]""", por005[0].GetMessage());
    }

    [Fact]
    public async Task POR005_Not_Report_When_Every_Argument_Has_A_Placeholder()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("cp {dest} {src}")]
    [CliCommandExample("cp a b")]
    public int Copy([CliArgument("source path")] string src, string dest) => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new CliArgumentParameterAnalyzer(), source);
    }

    [Fact]
    public async Task POR005_Stay_Silent_On_A_Parameter_With_No_CliArgument()
    {
        // 'dest' is an ordinary option parameter, not an argument. A rule that flagged every
        // parameter absent from the route would fail builds that work.
        var source = """
using Portico;

public class Svc
{
    [CliRoute("cp {src}")]
    [CliCommandExample("cp a --dest b")]
    public int Copy([CliArgument("source path")] string src, [CliOption("--dest")] string dest) => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new CliArgumentParameterAnalyzer(), source);
    }

    // ── Duplicate [CliArgument]: the compiler's job now, not an analyzer's ──────────────
    //
    // POR-79 retired POR007. It existed only because CliArgumentAttribute declared
    // AllowMultiple = true and the framework then banned what the attribute had just permitted.
    // AllowMultiple = false hands the check to the C# compiler as CS0579, which is strictly
    // stronger: no analyzer package to reference, no #pragma to suppress it, no way to turn it off.
    //
    // These tests are what makes the retirement safe rather than merely asserted — they pin the
    // replacement enforcement, both spellings, and the negative case.

    private static ImmutableArray<Diagnostic> Compile(string source) =>
        CSharpCompilation.Create(
                assemblyName: "DuplicateArgumentTest",
                syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
                references: AnalyzerTestRunner.MetadataReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .GetDiagnostics();

    [Theory]
    // Stacked attribute lists, and both attributes inside one list — the compiler must reject each.
    [InlineData("""[CliArgument("the path")] [CliArgument("another description")] string path""")]
    [InlineData("""[CliArgument("a"), CliArgument("b")] string path""")]
    public void Compiler_Rejects_Two_CliArguments_On_One_Parameter(string parameter)
    {
        var source = $$"""
using Portico;

public class Svc
{
    [CliRoute("init {path}")]
    [CliCommandExample("init .")]
    public int Init({{parameter}}) => 0;
}
""";

        var errors = Compile(source)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Contains(errors, d => d.Id == "CS0579");
    }

    [Fact]
    public void Compiler_Accepts_One_CliArgument_Per_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("cp {src} {dest}")]
    [CliCommandExample("cp a b")]
    public int Copy([CliArgument("source")] string src, [CliArgument("dest path")] string dest) => 0;
}
""";

        Assert.Empty(Compile(source).Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}

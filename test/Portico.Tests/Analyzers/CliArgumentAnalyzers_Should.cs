using System.Linq;
using System.Threading.Tasks;
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

    // ── POR007: parameter carries more than one [CliArgument] ──────────────

    [Fact]
    public async Task POR007_Report_When_Two_Attributes_Target_Same_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init {path}")]
    [CliCommandExample("init .")]
    public int Init([CliArgument("the path")] [CliArgument("another description")] string path) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateCliArgumentAnalyzer(), source);

        var por007 = diags.Where(d => d.Id == "POR007").ToArray();
        Assert.Single(por007);
        Assert.Contains("path", por007[0].GetMessage());
        Assert.Contains("Init", por007[0].GetMessage());
    }

    [Fact]
    public async Task POR007_Report_When_Both_Attributes_Share_One_Attribute_List()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init {path}")]
    [CliCommandExample("init .")]
    public int Init([CliArgument("a"), CliArgument("b")] string path) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateCliArgumentAnalyzer(), source);

        var por007 = diags.Where(d => d.Id == "POR007").ToArray();
        Assert.Single(por007);
        Assert.Contains("path", por007[0].GetMessage());
    }

    [Fact]
    public async Task POR007_Not_Report_When_Each_Parameter_Targeted_Once()
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

        await AnalyzerTestRunner.AssertCleanAsync(new DuplicateCliArgumentAnalyzer(), source);
    }
}

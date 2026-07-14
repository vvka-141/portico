using System.Linq;
using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliArgumentAnalyzers_Should
{
    // ── POR005: [CliArgument] references an unknown parameter ──────────────

    [Fact]
    public async Task SOL005_Report_When_Method_Level_Argument_Targets_Unknown_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("cp {dest}")]
    [CliArgument("src", "source path")]
    [CliCommandExample("cp a b")]
    public int Copy(string dest) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new CliArgumentParameterAnalyzer(), source);

        var sol005 = diags.Where(d => d.Id == "POR005").ToArray();
        Assert.Single(sol005);
        Assert.Contains("src", sol005[0].GetMessage());
        Assert.Contains("Copy", sol005[0].GetMessage());
        Assert.Contains("dest", sol005[0].GetMessage());
    }

    [Fact]
    public async Task SOL005_Not_Report_When_Method_Level_Argument_Targets_Existing_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("cp {dest}")]
    [CliArgument(nameof(src), "source path")]
    [CliCommandExample("cp a b")]
    public int Copy(string src, string dest) => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new CliArgumentParameterAnalyzer(), source);
    }

    // ── POR007: parameter targeted by more than one [CliArgument] ──────────

    [Fact]
    public async Task SOL007_Report_When_Method_And_Parameter_Level_Target_Same_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init")]
    [CliArgument(nameof(path), "the path")]
    [CliCommandExample("init .")]
    public int Init([CliArgument("the path")] string path) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateCliArgumentAnalyzer(), source);

        var sol007 = diags.Where(d => d.Id == "POR007").ToArray();
        Assert.Single(sol007);
        Assert.Contains("path", sol007[0].GetMessage());
        Assert.Contains("Init", sol007[0].GetMessage());
    }

    [Fact]
    public async Task SOL007_Report_When_Two_Parameter_Level_Attributes_Target_Same_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init")]
    [CliCommandExample("init .")]
    public int Init([CliArgument("a")][CliArgument("b")] string path) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new DuplicateCliArgumentAnalyzer(), source);

        var sol007 = diags.Where(d => d.Id == "POR007").ToArray();
        Assert.Single(sol007);
        Assert.Contains("path", sol007[0].GetMessage());
    }

    [Fact]
    public async Task SOL007_Not_Report_When_Each_Parameter_Targeted_Once()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("cp")]
    [CliArgument(nameof(src), "source")]
    [CliCommandExample("cp a b")]
    public int Copy(string src, [CliArgument("dest path")] string dest) => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new DuplicateCliArgumentAnalyzer(), source);
    }
}

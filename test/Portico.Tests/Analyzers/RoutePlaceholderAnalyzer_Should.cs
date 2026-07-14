using System.Linq;
using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class RoutePlaceholderAnalyzer_Should
{
    [Fact]
    public async Task Report_When_Placeholder_Has_No_Matching_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init {missing}")]
    [CliCommandExample("init .")]
    public int Init(string other) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new RoutePlaceholderAnalyzer(), source);

        var sol001 = diags.Where(d => d.Id == "POR001").ToArray();
        Assert.Single(sol001);
        Assert.Contains("missing", sol001[0].GetMessage());
        Assert.Contains("Init", sol001[0].GetMessage());
        Assert.Contains("other", sol001[0].GetMessage());
    }

    [Fact]
    public async Task Not_Report_When_Placeholder_Matches_Parameter()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init {path}")]
    [CliCommandExample("init .")]
    public int Init(string path) => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new RoutePlaceholderAnalyzer(), source);
    }

    [Fact]
    public async Task Report_Each_Missing_Placeholder_Separately()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("cp {src} {dest}")]
    [CliCommandExample("cp a b")]
    public int Copy(string source) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new RoutePlaceholderAnalyzer(), source);
        var sol001 = diags.Where(d => d.Id == "POR001").ToArray();
        Assert.Equal(2, sol001.Length);
        Assert.Contains(sol001, d => d.GetMessage().Contains("src"));
        Assert.Contains(sol001, d => d.GetMessage().Contains("dest"));
    }

    [Fact]
    public async Task Not_Report_On_Attribute_With_Same_Name_In_Other_Namespace()
    {
        var source = """
using System;

[AttributeUsage(AttributeTargets.Method)]
public class CliRouteAttribute : Attribute
{
    public CliRouteAttribute(string pattern) { }
}

public class Svc
{
    [CliRoute("init {missing}")]
    public int Init(string other) => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new RoutePlaceholderAnalyzer(), source);
    }

    [Fact]
    public async Task Not_Report_On_Method_Without_CliRoute()
    {
        var source = """
public class Svc
{
    public int Plain() => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new RoutePlaceholderAnalyzer(), source);
    }
}

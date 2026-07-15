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
    public async Task Not_Report_On_An_Embedded_Placeholder_The_Runtime_Treats_As_A_Literal()
    {
        // POR-61: the runtime splits the route on whitespace and matches each token against an
        // ANCHORED ^{name}$ regex, so "user{id}" is a literal token it routes fine — no placeholder,
        // no error. The analyzer must not fail this build.
        var source = """
using Portico;

public class Svc
{
    [CliRoute("get user{id}")]
    [CliCommandExample("get user42")]
    public int Get(string other) => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new RoutePlaceholderAnalyzer(), source);
    }

    [Fact]
    public async Task Report_Only_The_Whole_Token_Placeholder_Not_The_Embedded_One()
    {
        // "user{id}" is an embedded (literal) token; "{missing}" is a whole-token placeholder. Only
        // the latter must raise POR001 — proving the match is anchored per token, not a raw scan.
        var source = """
using Portico;

public class Svc
{
    [CliRoute("get user{id} {missing}")]
    [CliCommandExample("get user42 x")]
    public int Get(string other) => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new RoutePlaceholderAnalyzer(), source);
        var por001 = diags.Where(d => d.Id == "POR001").ToArray();
        Assert.Single(por001);   // only {missing}; the embedded {id} is not flagged
        Assert.Contains("missing", por001[0].GetMessage());
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

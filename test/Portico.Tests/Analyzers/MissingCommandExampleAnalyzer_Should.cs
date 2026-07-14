using System.Linq;
using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class MissingCommandExampleAnalyzer_Should
{
    [Fact]
    public async Task Warn_When_CliRoute_Method_Lacks_Example()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init")]
    public int Init() => 0;
}
""";

        var diags = await AnalyzerTestRunner.RunAsync(new MissingCommandExampleAnalyzer(), source);
        var sol004 = diags.Where(d => d.Id == "POR004").ToArray();
        Assert.Single(sol004);
        Assert.Contains("Init", sol004[0].GetMessage());
    }

    [Fact]
    public async Task Not_Warn_When_Example_Is_Present()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init")]
    [CliCommandExample("init")]
    public int Init() => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new MissingCommandExampleAnalyzer(), source);
    }

    [Fact]
    public async Task Not_Warn_On_Method_Without_CliRoute()
    {
        var source = """
public class Svc
{
    public int Plain() => 0;
}
""";

        await AnalyzerTestRunner.AssertCleanAsync(new MissingCommandExampleAnalyzer(), source);
    }
}

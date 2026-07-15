using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliCodeFixes_Should
{
    [Fact]
    public async Task POR004_Insert_CliCommandExample_Stub_That_Compiles()
    {
        var source = """
using Portico;

public class Svc
{
    [CliRoute("init")]
    public int Init() => 0;
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new MissingCommandExampleAnalyzer(),
            new MissingCommandExampleCodeFix(),
            source);

        Assert.Contains("[CliCommandExample(", fixedSource);
        Assert.Contains("CliRoute", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);
    }

    [Fact]
    public async Task POR006_Insert_Public_Parameterless_Constructor_That_Compiles()
    {
        var source = """
using Portico;

public sealed class MyOptions : CliOptions
{
    public MyOptions(string injected) { }
}
""";

        var fixedSource = await CodeFixTestRunner.ApplyAsync(
            new BundleCtorAnalyzer(),
            new BundleMissingCtorCodeFix(),
            source);

        Assert.Contains("public MyOptions()", fixedSource);
        CodeFixTestRunner.AssertCompiles(fixedSource);
    }
}

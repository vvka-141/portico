using System.Linq;
using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico.Analyzers;

// ReSharper disable once InconsistentNaming
public sealed class CliRouteReturnTypeAnalyzer_Should
{
    [Fact]
    public async Task Accept_Int_Return()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public int Run() => 0;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliRouteReturnTypeAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR008"));
    }

    [Fact]
    public async Task Accept_Task_Of_Int_Return()
    {
        const string source = """
            using System.Threading.Tasks;
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public Task<int> Run() => Task.FromResult(0);
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliRouteReturnTypeAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR008"));
    }

    [Fact]
    public async Task Flag_Void_Return()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public void Run() { }
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliRouteReturnTypeAnalyzer(), source);
        var sol008 = diags.Single(d => d.Id == "POR008");
        Assert.Contains("Run", sol008.GetMessage());
        Assert.Contains("void", sol008.GetMessage());
    }

    [Fact]
    public async Task Flag_Async_Void_Return()
    {
        const string source = """
            using System.Threading.Tasks;
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public async void Run() { await Task.Yield(); }
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliRouteReturnTypeAnalyzer(), source);
        Assert.Single(diags.Where(d => d.Id == "POR008"));
    }

    [Fact]
    public async Task Flag_NonGeneric_Task_Return()
    {
        const string source = """
            using System.Threading.Tasks;
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public Task Run() => Task.CompletedTask;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliRouteReturnTypeAnalyzer(), source);
        var sol008 = diags.Single(d => d.Id == "POR008");
        Assert.Contains("System.Threading.Tasks.Task", sol008.GetMessage());
    }

    [Fact]
    public async Task Flag_String_Return()
    {
        const string source = """
            using Portico;

            public sealed class S
            {
                [CliRoute("run")]
                [CliCommandExample("run")]
                public string Run() => "hello";
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliRouteReturnTypeAnalyzer(), source);
        Assert.Single(diags.Where(d => d.Id == "POR008"));
    }

    [Fact]
    public async Task Ignore_Methods_Without_CliRoute()
    {
        const string source = """
            public sealed class S
            {
                public void Helper() { }
                public string Name() => "";
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new CliRouteReturnTypeAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR008"));
    }
}

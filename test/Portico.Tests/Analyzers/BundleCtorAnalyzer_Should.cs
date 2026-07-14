using System.Linq;
using System.Threading.Tasks;
using Portico.Analyzers;
using Xunit;

namespace Portico.Analyzers;

// ReSharper disable once InconsistentNaming
public sealed class BundleCtorAnalyzer_Should
{
    [Fact]
    public async Task Accept_CliOptions_Subclass_With_Implicit_Parameterless_Ctor()
    {
        const string source = """
            using Portico;

            public sealed class MyOptions : CliOptions
            {
                [CliOption("--x")] public string? X { get; set; }
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new BundleCtorAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR006"));
    }

    [Fact]
    public async Task Accept_CliOptions_Subclass_With_Explicit_Public_Parameterless_Ctor()
    {
        const string source = """
            using Portico;

            public sealed class MyOptions : CliOptions
            {
                public MyOptions() { }
                public MyOptions(int capacity) { }
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new BundleCtorAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR006"));
    }

    [Fact]
    public async Task Flag_CliOptions_Subclass_Without_Parameterless_Ctor()
    {
        const string source = """
            using Portico;

            public sealed class BadBundle : CliOptions
            {
                public BadBundle(int requiredDependency) { }
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new BundleCtorAnalyzer(), source);
        var d = diags.Single(x => x.Id == "POR006");
        Assert.Contains("BadBundle", d.GetMessage());
        Assert.Contains("CliOptions", d.GetMessage());
    }

    // POR-26. This assertion was INVERTED. The ported rule flagged a ctor-injected middleware on
    // the premise that "middleware are instantiated per-invocation via Activator.CreateInstance" —
    // which is false for middleware. CliMiddleware.Clone() is MemberwiseClone(), and middleware
    // only ever reaches the framework through UseMiddleware(instance), which the USER constructs.
    // The runtime has always supported ctor injection; only the analyzer forbade it, which blocked
    // the ordinary DI shape UseMiddleware(sp.GetRequiredService<T>()).
    //
    // A ported test asserting a wrong rule is still a wrong rule.
    [Fact]
    public async Task Accept_CliMiddleware_Subclass_With_A_Constructor_Dependency()
    {
        const string source = """
            using Portico;

            public interface IAuditLog { void Write(string message); }

            public sealed class AuditMiddleware : CliMiddleware
            {
                private readonly IAuditLog _log;
                public AuditMiddleware(IAuditLog log) => _log = log;
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new BundleCtorAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR006"));
    }

    [Fact]
    public async Task Ignore_Abstract_CliOptions_Subclass()
    {
        const string source = """
            using Portico;

            public abstract class AbstractBundle : CliOptions
            {
                protected AbstractBundle(int x) { }
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new BundleCtorAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR006"));
    }

    [Fact]
    public async Task Flag_Private_Parameterless_Ctor()
    {
        const string source = """
            using Portico;

            public sealed class PrivateCtor : CliOptions
            {
                private PrivateCtor() { }
                public PrivateCtor(int x) { }
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new BundleCtorAnalyzer(), source);
        Assert.Single(diags.Where(d => d.Id == "POR006"));
    }

    [Fact]
    public async Task Ignore_Unrelated_Class()
    {
        const string source = """
            public sealed class NotABundle
            {
                public NotABundle(int x) { }
            }
            """;

        var diags = await AnalyzerTestRunner.RunAsync(new BundleCtorAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id == "POR006"));
    }
}

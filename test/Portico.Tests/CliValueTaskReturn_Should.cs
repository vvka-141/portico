using System.Threading.Tasks;
using Xunit;

namespace Portico;

// POR-64. POR008 forbids a ValueTask return type, but the runtime is a deliberately lenient backstop
// (it already handles non-generic Task more permissively than the analyzer). If POR008 is suppressed
// or absent, a ValueTask handler must still be AWAITED and its exit code honoured — not silently
// discarded as 0 (which would report success for unfinished async work). The analyzers are referenced
// as ordinary assemblies here, not active plugins, so these ValueTask routes compile — precisely the
// "POR008 absent" scenario.
// ReSharper disable once InconsistentNaming
public sealed class CliValueTaskReturn_Should
{
    public sealed class Svc
    {
        public bool Ran { get; private set; }

        [CliRoute("vt-int")]
        [CliCommandExample("vt-int")]
        public async ValueTask<int> VtInt()
        {
            await Task.Yield();
            Ran = true;
            return 7;
        }

        [CliRoute("vt")]
        [CliCommandExample("vt")]
        public async ValueTask Vt()
        {
            await Task.Yield();
            Ran = true;
        }
    }

    private static (int exit, Svc svc) Run(string commandLine)
    {
        var svc = new Svc();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));
        return (app.Run(commandLine), svc);
    }

    [Fact]
    public void Await_A_ValueTask_Of_Int_And_Honour_Its_Exit_Code()
    {
        var (exit, svc) = Run("app.exe vt-int");

        Assert.True(svc.Ran, "the ValueTask<int> handler must be awaited to completion");
        Assert.Equal(7, exit);   // not 0 — the exit code is honoured, not discarded
    }

    [Fact]
    public void Await_A_Plain_ValueTask_And_Return_Zero()
    {
        var (exit, svc) = Run("app.exe vt");

        Assert.True(svc.Ran, "the ValueTask handler must be awaited to completion");
        Assert.Equal(0, exit);
    }
}

using System.Threading.Tasks;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Guards the Main(string[] args) contract: the argv parameter in a C# entry point OMITS
// the executable name, while Environment.GetCommandLineArgs()[0] INCLUDES it. The framework
// prepends the exe name automatically when Run(string[] args) is called so users can wire
// `public static int Main(string[] args) => App.Create(...).Run(args);` without footguns.
public sealed class CliApplicationRunArgs_Should
{
    public sealed class CountService
    {
        public int Observed { get; private set; } = -1;

        [CliRoute("count {n}")]
        [CliCommandExample("count 3")]
        public int Count([CliArgument("how many")] int n)
        {
            Observed = n;
            return 0;
        }
    }

    [Fact]
    public void Accept_Main_Shape_Args_Array_Without_Exe_Name()
    {
        var svc = new CountService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        // Simulates `Main(string[] args)` where args does NOT include the exe name.
        int exit = app.Run(new[] { "count", "42" });

        Assert.Equal(0, exit);
        Assert.Equal(42, svc.Observed);
    }

    [Fact]
    public async Task Accept_Main_Shape_Args_Array_Async()
    {
        var svc = new CountService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        int exit = await app.RunAsync(new[] { "count", "7" });

        Assert.Equal(0, exit);
        Assert.Equal(7, svc.Observed);
    }

    [Fact]
    public void Accept_Empty_Args_Array_And_Show_General_Help()
    {
        var svc = new CountService();
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc).WithConsole(console));

        // Main with no args — general help is shown; exit 0 (not a usage error).
        int exit = app.Run(System.Array.Empty<string>());

        Assert.Equal(0, exit);
        Assert.Contains("count", console.OutWriter.ToString());
    }
}

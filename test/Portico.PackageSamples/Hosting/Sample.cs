// The code sample in src/Portico.Hosting/PACKAGE-README.md, compiled against the Hosting package
// alone (POR-155).

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Portico;
using Portico.Hosting;

namespace Portico.PackageSamples.Hosting;

public interface IAdminTool
{
    [CliRoute("health")]
    [CliCommandExample("health")]
    int Health();
}

public sealed class AdminTool : IAdminTool
{
    public int Health() => 0;
}

public static class Sample
{
    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddPorticoCommands<IAdminTool, AdminTool>();

        return await builder.Build().RunPorticoAsync(args);
    }
}

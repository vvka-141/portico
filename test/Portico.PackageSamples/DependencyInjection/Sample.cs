// The code sample in src/Portico.DependencyInjection/PACKAGE-README.md, compiled against ONLY the
// packages that README tells the reader to install.
//
// Two defects shipped in 0.1.0 and again in 0.1.1 because nothing compiled it this way (POR-155):
//
//   1. It called BuildServiceProvider(), an extension method in
//      Microsoft.Extensions.DependencyInjection — a package the adapter deliberately does NOT
//      depend on, because depending only on .Abstractions is what keeps the graph honest. A reader
//      installing exactly what the README listed got CS1061.
//
//   2. It showed no `using` directives at all. Without `using Portico.DependencyInjection;` the
//      IServiceProvider overload is not in scope, the compiler binds to the CORE package's
//      AddCommands<T>(Func<T>), and the error names Func<IAdminTool> — a type appearing nowhere in
//      the sample, on a line the reader has no reason to suspect.
//
// The sample now shows the shape a reader actually has: the IServiceProvider their service already
// built. That needs no extra package and is the realistic case for the stated audience.

using System;
using Portico;
using Portico.DependencyInjection;

namespace Portico.PackageSamples.Di;

public interface IAdminTool
{
    [CliRoute("health")]
    [CliCommandExample("health")]
    int Health();
}

public sealed class AdminTool : IAdminTool
{
    public int Health()
    {
        Console.WriteLine("healthy");
        return 0;
    }
}

public static class Sample
{
    public static int Run(IServiceProvider services, string[] args) =>
        CliApplication
            .Create(cfg => cfg.AddCommands<IAdminTool>(services))
            .Run(args);
}

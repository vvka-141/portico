using Portico;

namespace AdminCli;

public static class Program
{
    // The whole wiring. Ctrl+C -> exit 130 and SIGTERM -> exit 143 are wired for you.
    public static int Main(string[] args) =>
        CliApplication
            .Create(cfg => cfg
                .AddCommands(new AdminTool())
                .WithVersion("admin 1.0.0")
                .UseMiddleware(new CliTimingMiddleware()))
            .Run(args);
}

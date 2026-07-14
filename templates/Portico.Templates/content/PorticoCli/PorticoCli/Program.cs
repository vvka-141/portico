using Portico;

namespace PorticoCli;

public static class Program
{
    // The whole wiring. Ctrl+C -> exit 130 and SIGTERM -> exit 143 are handled for you.
    public static int Main(string[] args) =>
        CliApplication
            .Create(cfg => cfg
                .AddCommands(new GreetTool())
                .WithVersion("PorticoCli 1.0.0"))
            .Run(args);
}

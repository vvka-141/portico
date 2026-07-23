using Portico;

namespace ReferenceCli;

/// <summary>
/// The composition root. It wires three things and runs:
/// <list type="bullet">
///   <item>the main <see cref="IFleetTool"/> surface, its handler built per invocation by a factory
///     (the dependency-injection hook — return a container-resolved instance here in a real app);</item>
///   <item>a second contract, <see cref="IDiagnosticsTool"/>, mounted under the literal prefix
///     <c>diag</c> so its <c>health</c> route answers to <c>fleet diag health</c>;</item>
///   <item>an <see cref="AuditMiddleware"/> constructed with its injected sink and applied to every
///     route.</item>
/// </list>
/// </summary>
/// <example><code>
/// fleet worker list eu-west --status idle -v
/// fleet job submit --queue orders --priority 7 --auth-token "$TOKEN"
/// fleet diag health
/// </code></example>
public static class Program
{
    public static int Main(string[] args)
    {
        // A real app resolves these from its container. Plain construction shows the shape without
        // pulling in Portico.DependencyInjection.
        IClock clock = new SystemClock();
        IAuditSink audit = new ConsoleAuditSink();

        return CliApplication
            .Create(cfg => cfg
                // The factory runs once per matched invocation — the DI seam. Only the invoked
                // command's handler is constructed; --help and unknown routes construct nothing.
                .AddCommands(() => new FleetTool(clock))
                // A second contract mounted under a literal prefix. It never had to know the prefix.
                .AddCommands(new DiagnosticsTool(), [new CliRouteAttribute("diag")])
                // Middleware with a constructor dependency, constructed here and handed in.
                .UseMiddleware(new AuditMiddleware(audit))
                .WithVersion("fleet 1.0.0"))
            .Run(args);
    }
}

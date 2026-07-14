using Platform.Queue;
using Platform.Storage;
using Portico;

namespace PlatformCli;

/// <summary>
/// The master CLI. It owns no routes of its own — it mounts two independently-built contracts under
/// a prefix each, and that is the whole composition story:
/// <code>
/// platform storage status --bucket invoices
/// platform queue   status --queue orders
/// </code>
/// Both contracts declare a route literally called <c>status</c>. They never had to agree, because
/// the mount point — not the route name — is what disambiguates them. Each team ships, versions and
/// contract-tests its own tool; the platform team decides where it hangs.
/// </summary>
public static class Program
{
    public static int Main(string[] args) =>
        CliApplication
            .Create(cfg => cfg
                .AddCommands(new StorageTool(), [new CliRouteAttribute("storage")])
                .AddCommands(new QueueTool(), [new CliRouteAttribute("queue")])
                .WithVersion("platform 1.0.0"))
            .Run(args);
}

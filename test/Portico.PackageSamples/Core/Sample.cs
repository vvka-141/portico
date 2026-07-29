// The code sample in src/Portico/PACKAGE-README.md, plus one fragment per capability the README
// claims, compiled against the core package alone (POR-155). A capability named on a NuGet page is
// a promise; this is what keeps the fragments from drifting away from it.

using System;
using System.Collections.Immutable;
using Portico;

namespace Portico.PackageSamples.Core;

public interface IAdminTool
{
    [CliRoute("db migrate")]
    [CliCommandExample("db migrate --connection-string \"Host=db\"")]
    int Migrate(
        // Sensitive: the value is withheld from every message the framework composes.
        [CliOption("--connection-string|-c", "Postgres connection string", Sensitive = true)]
        string connectionString,
        // A duration the way an operator types it: "30 seconds", "90s", "1h30m", "PT30S".
        [CliOption("--timeout", "How long to wait")] TimeSpan? timeout = null,
        // 17 collection shapes bind, immutable ones included.
        [CliOption("--tables", "Tables to migrate")] ImmutableArray<string> tables = default);
}

public sealed class AdminTool : IAdminTool
{
    public int Migrate(string connectionString, TimeSpan? timeout, ImmutableArray<string> tables)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Named POSIX exit codes, so a pipeline can tell "misconfigured" from "failed".
            throw new CliExitException("No connection string.")
            {
                ExitCode = CliExitException.UsageErrorExitCode,
            };
        }

        Console.WriteLine($"migrating {tables.Length} table(s)");
        return 0;
    }
}

public static class Sample
{
    public static int Run(string[] args) =>
        CliApplication.Create(cfg => cfg.AddCommands(new AdminTool())).Run(args);
}

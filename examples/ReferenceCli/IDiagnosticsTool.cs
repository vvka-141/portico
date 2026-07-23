using System;
using System.ComponentModel;
using Portico;

namespace ReferenceCli;

/// <summary>
/// A second, independently-authored contract, mounted under <c>diag</c> by the composition root.
/// It knows nothing about the mount prefix — its routes are <c>health</c> and <c>version</c>, and it
/// is the <c>Program</c> that decides they hang under <c>fleet diag …</c>. This is the composition
/// story: separate contracts, each contract-tested on its own, assembled by whoever ships the binary.
/// </summary>
public interface IDiagnosticsTool
{
    /// <summary>Report health. Exit 0 = healthy, 1 = unhealthy — usable straight from a HEALTHCHECK.</summary>
    [Description("Report service health (exit 0 = healthy)")]
    [CliRoute("health")]
    [CliCommandExample("health")]
    int Health();

    /// <summary>Print the build version.</summary>
    [Description("Print the build version")]
    [CliRoute("version")]
    [CliCommandExample("version")]
    int Version();
}

/// <summary>Implementation behind <see cref="IDiagnosticsTool"/>.</summary>
public sealed class DiagnosticsTool : IDiagnosticsTool
{
    public int Health()
    {
        Console.WriteLine("healthy");
        return 0;
    }

    public int Version()
    {
        Console.WriteLine("fleet 1.0.0");
        return 0;
    }
}

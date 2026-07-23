using System.ComponentModel;
using Portico;

namespace Platform.Storage;

/// <summary>
/// The storage team's CLI, built and shipped on its own. It knows nothing about the master CLI that
/// will mount it, and nothing about the queue team's tool — including the fact that both declare a
/// route called <c>status</c>. The mount point resolves that, not a conversation between the teams.
/// </summary>
public interface IStorageTool
{
    /// <summary>Report bucket health.</summary>
    [Description("Report bucket health")]
    [CliRoute("status")]
    [CliCommandExample("status")]
    [CliCommandExample("status --bucket invoices")]
    int Status([CliOption("--bucket", "Limit the report to one bucket")] string? bucket = null);

    /// <summary>Delete objects older than a retention window.</summary>
    [Description("Delete objects older than a retention window")]
    [CliRoute("purge {bucket}")]
    [CliCommandExample("purge invoices --older-than \"30 days\"")]
    int Purge(
        [CliArgument("which bucket to purge")] string bucket,
        [CliOption("--older-than", "Delete objects older than this")] System.TimeSpan? olderThan = null);
}

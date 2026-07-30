using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Portico;

// POR-111. The `publish` job in release.yml can mint a nuget.org publishing credential, and an
// upload to nuget.org is immutable. Trusted Publishing (POR-138) removed the long-lived secret, which
// removed something to steal — it did not change who is authorised. Before the `environment:` key,
// the complete check for an irreversible publish to a public registry was "can you push a tag
// matching v*".
//
// Two coupled facts have to stay true and cannot be checked by building anything:
//
//   1. the publish job declares an environment — deleting that line is a one-character-looking edit
//      that silently widens the OIDC token's audience back out;
//   2. the header comment names the SAME environment, because that comment is the setup instruction
//      for the nuget.org policy field, and the two must agree or the token exchange fails. POR-111
//      called this "the step most likely to be forgotten", and it was right: the comment previously
//      said "leave empty — this workflow uses none", which was correct until it wasn't.
//
// Same shape as CliShortOptionDocs_Should and PorticoAnalyzerDocs_Should: read the tracked file,
// compare it against itself, and fail the build when the two halves disagree.
// ReSharper disable once InconsistentNaming
public sealed class ReleaseWorkflow_Should
{
    private const string WorkflowPath = ".github/workflows/release.yml";

    /// <summary>
    /// The environment the publish job must run in. Named here as well so a rename is a deliberate
    /// three-place edit — workflow, comment, test — rather than a quiet one.
    /// </summary>
    private const string ExpectedEnvironment = "release";

    [Fact]
    public void Gate_The_Publish_Job_Behind_An_Environment()
    {
        var declared = EnvironmentOfJob("publish");

        Assert.False(
            declared is null,
            $"The 'publish' job in {WorkflowPath} declares no `environment:`. That job mints a " +
            "nuget.org credential for an immutable upload; without an environment, GitHub has nowhere " +
            "to require a reviewer and the nuget.org policy cannot narrow past 'any run of this " +
            "workflow'. Restore it, or change this test deliberately and say why (POR-111).");

        Assert.Equal(ExpectedEnvironment, declared);
    }

    [Fact]
    public void Instruct_The_Reader_To_Set_The_Same_Environment_On_The_NuGet_Policy()
    {
        var header = string.Join(
            Environment.NewLine,
            File.ReadAllLines(WorkflowFile()).TakeWhile(line => !line.StartsWith("on:", StringComparison.Ordinal)));

        // The header is the setup instruction for the nuget.org Trusted Publishing policy. If it does
        // not name the environment the job actually uses, the next person configuring that policy
        // either leaves the field empty (no narrowing — the defect POR-111 was filed for) or sets a
        // name that does not match (the token exchange then fails).
        Assert.Contains(
            $"Environment:       {ExpectedEnvironment}",
            header,
            StringComparison.Ordinal);

        Assert.DoesNotContain("leave empty", header, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The value of <c>environment:</c> inside the named job, or <see langword="null"/> if the job
    /// declares none.
    /// </summary>
    /// <remarks>
    /// Hand-parsed rather than pulled through a YAML package: the core has zero dependencies and the
    /// test project should not be the place a YAML parser sneaks in for two keys. Scoping is by
    /// indentation — a job's keys are indented further than its own name, so the block ends at the
    /// first line indented no further than the job key itself.
    /// </remarks>
    private static string? EnvironmentOfJob(string jobName)
    {
        var lines = File.ReadAllLines(WorkflowFile());

        var start = Array.FindIndex(lines, line => line.TrimEnd() == $"  {jobName}:");
        Assert.True(start >= 0, $"{WorkflowPath} has no '{jobName}:' job. If it was renamed, update this test.");

        var jobIndent = Indent(lines[start]);

        foreach (var line in lines.Skip(start + 1))
        {
            if (line.Trim().Length == 0) continue;
            if (line.TrimStart().StartsWith('#')) continue;
            if (Indent(line) <= jobIndent) break;               // next job, or back out to the top level

            var trimmed = line.Trim();
            if (trimmed.StartsWith("environment:", StringComparison.Ordinal))
            {
                return trimmed["environment:".Length..].Trim();
            }

            // `steps:` opens the job's body; an `environment:` under a step is a different key with a
            // different meaning, so stop before reading one by accident.
            if (trimmed == "steps:") break;
        }

        return null;
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    private static string WorkflowFile() => Path.Combine(RepositoryRoot(), WorkflowPath);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "portico.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}

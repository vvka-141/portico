using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Portico.Packaging;

/// <summary>
/// Asserts against the packed .nupkg files, not the source that produces them.
/// A source-level test cannot catch a nuspec dependency that the code never references,
/// which is exactly the failure mode the zero-dependency claim exists to guard against.
/// </summary>
public sealed class NupkgContents_Should
{
    private static readonly string ArtifactsDir = GetArtifactsDir();

    private static string GetArtifactsDir()
    {
        var dir = Environment.GetEnvironmentVariable("PORTICO_ARTIFACTS");
        if (string.IsNullOrEmpty(dir))
            throw new InvalidOperationException(
                "PORTICO_ARTIFACTS environment variable is not set. " +
                "Run: dotnet test test/Portico.Packaging.Tests -c Release -e PORTICO_ARTIFACTS=./artifacts");
        var full = Path.GetFullPath(dir);
        if (!Directory.Exists(full))
            throw new InvalidOperationException(
                $"Artifacts directory does not exist: {full}. Run dotnet pack first.");
        return full;
    }

    private static string FindNupkg(string packageId)
    {
        var matches = Directory.GetFiles(ArtifactsDir, $"{packageId}.*.nupkg")
            .Where(f => !f.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return name.Length > packageId.Length + 1 && char.IsDigit(name[packageId.Length + 1]);
            })
            .ToArray();
        Assert.True(matches.Length == 1,
            $"Expected exactly one {packageId} nupkg in {ArtifactsDir}, found {matches.Length}: " +
            string.Join(", ", matches.Select(Path.GetFileName)));
        return matches[0];
    }

    private static XElement ReadNuspec(string nupkgPath)
    {
        using var zip = ZipFile.OpenRead(nupkgPath);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        return XElement.Load(stream);
    }

    private static XNamespace NuspecNs(XElement nuspec)
    {
        return nuspec.Name.Namespace;
    }

    [Fact]
    public void HaveNoDependenciesInTheCorePackage()
    {
        var nupkg = FindNupkg("Portico");
        var nuspec = ReadNuspec(nupkg);
        var ns = NuspecNs(nuspec);

        var groups = nuspec.Descendants(ns + "dependencies")
            .Elements(ns + "group")
            .ToList();

        Assert.NotEmpty(groups);
        foreach (var group in groups)
        {
            var tfm = group.Attribute("targetFramework")?.Value ?? "(no TFM)";
            var deps = group.Elements(ns + "dependency").ToList();
            Assert.True(deps.Count == 0,
                $"The core Portico package must have zero dependencies, but the {tfm} group declares: " +
                string.Join(", ", deps.Select(d => d.Attribute("id")?.Value)));
        }
    }

    [Fact]
    public void PackTheAgentAssetAtBothLocations()
    {
        var nupkg = FindNupkg("Portico");
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("buildTransitive/PORTICO-FOR-AGENTS.md", entries);
        Assert.Contains("PORTICO-FOR-AGENTS.md", entries);
    }

    [Fact]
    public void PackTheBuildTransitiveProps()
    {
        var nupkg = FindNupkg("Portico");
        using var zip = ZipFile.OpenRead(nupkg);
        var entries = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("buildTransitive/Portico.props", entries);
    }

    [Fact]
    public void PackTheAnalyzerAssemblies()
    {
        var nupkg = FindNupkg("Portico");
        using var zip = ZipFile.OpenRead(nupkg);
        var analyzerDlls = zip.Entries
            .Where(e => e.FullName.StartsWith("analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase)
                     && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName)
            .ToList();

        Assert.True(analyzerDlls.Count >= 2,
            $"Expected at least Portico.Analyzers.dll and Portico.Analyzers.CodeFixes.dll under analyzers/dotnet/cs/, " +
            $"found {analyzerDlls.Count}: {string.Join(", ", analyzerDlls)}");
    }

    /// <summary>
    /// Every shipped package carries the same icon and README (POR-135). Both are declared once
    /// in Directory.Build.props, and a package that opts out of the shared item groups would lose
    /// them silently: nuget.org renders a placeholder rather than an error, so nothing fails until
    /// a visitor notices. NuGet caps an embedded icon at 1 MB, which is checked here too — the
    /// generated icon is three orders of magnitude under it, and the assertion exists so that a
    /// hand-dropped replacement cannot quietly break the pack.
    /// </summary>
    [Theory]
    [InlineData("Portico")]
    [InlineData("Portico.DependencyInjection")]
    [InlineData("Portico.Hosting")]
    [InlineData("Portico.Templates")]
    public void PackTheSharedIconAndReadme(string packageId)
    {
        const string IconFile = "portico-icon-128.png";
        const long NuGetIconSizeLimit = 1024 * 1024;

        var nupkg = FindNupkg(packageId);
        var nuspec = ReadNuspec(nupkg);
        var ns = NuspecNs(nuspec);

        Assert.Equal(IconFile, nuspec.Descendants(ns + "icon").SingleOrDefault()?.Value);
        Assert.Equal("PACKAGE-README.md", nuspec.Descendants(ns + "readme").SingleOrDefault()?.Value);

        using var zip = ZipFile.OpenRead(nupkg);
        var icon = zip.Entries.SingleOrDefault(e =>
            string.Equals(e.FullName, IconFile, StringComparison.OrdinalIgnoreCase));

        Assert.True(icon is not null,
            $"{packageId} declares <icon>{IconFile}</icon> but does not contain the file. " +
            "nuget.org shows a placeholder rather than failing, so this is the only place it surfaces.");
        Assert.True(icon!.Length is > 0 and < NuGetIconSizeLimit,
            $"{IconFile} in {packageId} is {icon.Length} bytes; NuGet rejects an embedded icon at or above {NuGetIconSizeLimit}.");
    }

    [Theory]
    [InlineData("Portico.DependencyInjection")]
    [InlineData("Portico.Hosting")]
    public void FlowAnalyzersFromAdapterPackages(string adapterId)
    {
        var nupkg = FindNupkg(adapterId);
        var nuspec = ReadNuspec(nupkg);
        var ns = NuspecNs(nuspec);

        var groups = nuspec.Descendants(ns + "dependencies")
            .Elements(ns + "group")
            .ToList();

        Assert.NotEmpty(groups);
        foreach (var group in groups)
        {
            var tfm = group.Attribute("targetFramework")?.Value ?? "(no TFM)";
            var porticoDep = group.Elements(ns + "dependency")
                .FirstOrDefault(d => string.Equals(d.Attribute("id")?.Value, "Portico", StringComparison.OrdinalIgnoreCase));

            Assert.True(porticoDep is not null,
                $"{adapterId} {tfm} group does not declare a dependency on Portico.");

            var exclude = porticoDep!.Attribute("exclude")?.Value ?? "";
            Assert.False(
                exclude.Contains("Analyzers", StringComparison.OrdinalIgnoreCase),
                $"{adapterId} {tfm} group excludes Analyzers from the Portico dependency — " +
                "POR001–POR010 would be silently off for adapter-only consumers (POR-53).");
        }
    }

    [Theory]
    [InlineData("Portico.DependencyInjection", "net8.0", "Microsoft.Extensions.DependencyInjection.Abstractions", "8.")]
    [InlineData("Portico.DependencyInjection", "net10.0", "Microsoft.Extensions.DependencyInjection.Abstractions", "10.")]
    [InlineData("Portico.Hosting", "net8.0", "Microsoft.Extensions.Hosting", "8.")]
    [InlineData("Portico.Hosting", "net10.0", "Microsoft.Extensions.Hosting", "10.")]
    public void DeclareCorrectExtensionsFloorPerTfm(
        string adapterId, string expectedTfm, string extensionPkg, string expectedMajor)
    {
        var nupkg = FindNupkg(adapterId);
        var nuspec = ReadNuspec(nupkg);
        var ns = NuspecNs(nuspec);

        var group = nuspec.Descendants(ns + "dependencies")
            .Elements(ns + "group")
            .FirstOrDefault(g => (g.Attribute("targetFramework")?.Value ?? "").Contains(expectedTfm, StringComparison.OrdinalIgnoreCase));

        Assert.True(group is not null, $"{adapterId} has no dependency group for {expectedTfm}");

        var dep = group!.Elements(ns + "dependency")
            .FirstOrDefault(d => string.Equals(d.Attribute("id")?.Value, extensionPkg, StringComparison.OrdinalIgnoreCase));

        Assert.True(dep is not null, $"{adapterId} {expectedTfm} group does not declare {extensionPkg}");

        var version = dep!.Attribute("version")?.Value ?? "";
        Assert.True(version.StartsWith(expectedMajor, StringComparison.Ordinal),
            $"{adapterId} {expectedTfm}: expected {extensionPkg} >= {expectedMajor}x, got {version}");
    }
}

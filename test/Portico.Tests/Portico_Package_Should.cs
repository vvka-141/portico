using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Portico;

/// <summary>
/// Guards the invariants the scaffold exists to protect. The zero-dependency rule
/// is a positioning claim as much as a technical one — a stray package reference
/// would break it silently, so it is asserted, not assumed.
/// </summary>
public sealed class Portico_Package_Should
{
    /// <summary>
    /// Assemblies that ship with the runtime itself. A reference to anything outside
    /// this set means the Portico package acquired a NuGet dependency.
    /// </summary>
    private static bool IsFrameworkAssembly(string name) =>
        name == "System" ||
        name == "mscorlib" ||
        name == "netstandard" ||
        name.StartsWith("System.", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.CSharp", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.VisualBasic", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal);

    [Fact]
    public void HaveNoDependencies()
    {
        var portico = Assembly.Load("Portico");

        IReadOnlyList<string> external = portico
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !IsFrameworkAssembly(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            external.Count == 0,
            $"The core Portico package must have zero dependencies, but it references: {string.Join(", ", external)}. " +
            "Anything needing Microsoft.Extensions.* belongs in Portico.DependencyInjection or Portico.Hosting.");
    }

    /// <summary>
    /// Every adapter package must flow the analyzers from the core (POR-53).
    /// </summary>
    /// <remarks>
    /// NuGet's default for a package dependency is <c>PrivateAssets="contentfiles;analyzers;build"</c>
    /// — analyzer assets do <b>not</b> flow transitively. Without an explicit
    /// <c>PrivateAssets="none"</c>, <c>dotnet add package Portico.DependencyInjection</c> gives you the
    /// framework with POR001–POR010 silently switched off: a green build, and none of the compile-time
    /// checks the README promises. Nothing observable breaks, which is exactly why it needs a test.
    /// The end-to-end proof is consuming the packed <c>.nupkg</c>; this guards the setting that makes
    /// it true.
    /// </remarks>
    [Theory]
    [InlineData("src/Portico.DependencyInjection/Portico.DependencyInjection.csproj")]
    [InlineData("src/Portico.Hosting/Portico.Hosting.csproj")]
    public void FlowTheAnalyzersFromEveryAdapterPackage(string projectPath)
    {
        var csproj = File.ReadAllText(Path.Combine(RepositoryRoot(), projectPath));

        var references = Regex.Matches(csproj, @"<ProjectReference\b[^>]*?/>", RegexOptions.Singleline)
            .Select(match => match.Value)
            .Where(reference => reference.Contains("Portico", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(references);
        Assert.All(references, reference =>
            Assert.True(
                reference.Contains("PrivateAssets=\"none\"", StringComparison.Ordinal),
                $"{projectPath} references a Portico project without PrivateAssets=\"none\":{Environment.NewLine}" +
                $"{reference}{Environment.NewLine}" +
                "NuGet does not flow analyzer assets transitively, so a consumer who installs only this " +
                "adapter would get the framework with the analyzers silently off (POR-53)."));
    }

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

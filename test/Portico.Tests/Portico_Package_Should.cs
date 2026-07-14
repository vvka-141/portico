using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
}

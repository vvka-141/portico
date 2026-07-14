using System;
using System.Reflection;

namespace Portico;

/// <summary>
/// Single source of truth for entry-assembly version discovery, shared by the parameterless
/// <see cref="ICliApplicationBuilder.WithVersion()"/> and the framework's default version factory
/// (SOL-82: the two were byte-identical reimplementations that would drift). Prefers
/// <see cref="AssemblyInformationalVersionAttribute"/> (so build metadata declared via
/// <c>&lt;Version&gt;</c> / <c>&lt;InformationalVersion&gt;</c> flows through), falling back to
/// <see cref="AssemblyName.Version"/>.
/// </summary>
internal static class CliVersionDiscovery
{
    /// <summary>
    /// Returns <c>"{assembly-name} {version}"</c> discovered from the entry assembly, or
    /// <c>"unknown 0.0.0"</c> when there is no entry assembly (e.g. some hosting scenarios).
    /// </summary>
    public static string FromEntryAssembly()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry is null) return "unknown 0.0.0";

        var name = entry.GetName().Name ?? "app";
        var informational = entry
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return $"{name} {informational}";
        }

        var assemblyVersion = entry.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion)
            ? $"{name} 0.0.0"
            : $"{name} {assemblyVersion}";
    }
}

using System;
using System.IO;

namespace Portico;

/// <summary>
/// Output, error, and input channels a <see cref="CliApplication"/> writes to and reads from.
/// Default implementation (<see cref="SystemCliConsole"/>) proxies to <see cref="Console"/>.
/// Replace during testing or hosting to capture output or redirect input without mutating
/// global <c>Console.Out</c>/<c>Console.Error</c>.
/// </summary>
public interface ICliConsole
{
    /// <summary>Standard output. Help text and non-error diagnostics go here.</summary>
    TextWriter Out { get; }

    /// <summary>Standard error. User-visible exceptions and "did you mean" hints go here.</summary>
    TextWriter Error { get; }

    /// <summary>Standard input. Interactive prompts read from this.</summary>
    TextReader In { get; }

    /// <summary>
    /// True when stdout has been redirected to a file or pipe — i.e., not attached to an
    /// interactive terminal. Consumers that emit TTY-only decoration (colors, progress spinners,
    /// ANSI escapes) should check this before rendering. Defaults to <c>false</c> on custom
    /// implementations; <see cref="SystemCliConsole"/> reports the real process state.
    /// </summary>
    bool IsOutputRedirected => false;

    /// <summary>
    /// True when stdin has been redirected from a file, pipe, or in-memory reader. Prompt helpers
    /// use this independently from <see cref="IsOutputRedirected"/>: redirecting output does not
    /// imply that password input can be read with <see cref="Console.ReadKey(bool)"/>.
    /// Custom implementations default to <c>false</c>.
    /// </summary>
    bool IsInputRedirected => false;

    /// <summary>
    /// True when the console is suitable for colored / styled output. The default implementation
    /// honors the <c>NO_COLOR</c> environment variable (see <c>https://no-color.org</c>) and the
    /// absence of a TTY — consumers that respect this property automatically degrade gracefully
    /// in CI, piped contexts, and explicitly color-disabled environments. Custom implementations
    /// default to <c>false</c> (color opt-in).
    /// </summary>
    bool IsColorEnabled => false;
}

/// <summary>
/// Default <see cref="ICliConsole"/> wired to the process's <see cref="Console"/> streams.
/// </summary>
public sealed class SystemCliConsole : ICliConsole
{
    /// <summary>The singleton instance. Stateless; safe to share.</summary>
    public static readonly SystemCliConsole Instance = new();

    private SystemCliConsole() { }

    public TextWriter Out => Console.Out;
    public TextWriter Error => Console.Error;
    public TextReader In => Console.In;

    public bool IsOutputRedirected => Console.IsOutputRedirected;
    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool IsColorEnabled =>
        !Console.IsOutputRedirected &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
}

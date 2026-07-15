using System;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-60. A positional-argument error must be sanitized on the same stderr boundary as an option
// error. Argument errors used to throw CliExitException with the raw argv token interpolated in, so
// ANSI escapes and zero-width codepoints reached the terminal / an agent's context window — the
// prompt-injection channel the option path (CliOptionMaterializationException) already closed.
// ReSharper disable once InconsistentNaming
public sealed class CliArgumentSanitization_Should
{
    public interface ITool
    {
        [CliRoute("square")]
        [CliCommandExample("square 5")]
        int Square([CliArgument(nameof(n), "the number to square")] int n);

        [CliRoute("scale")]
        [CliCommandExample("scale --by 2")]
        int Scale([CliOption("--by")] int by);
    }

    private sealed class Tool : ITool
    {
        public int Square(int n) => 0;
        public int Scale(int by) => 0;
    }

    private static CliTestRunResult Run(string commandLine) =>
        CliTestHarness.ForApplication(cfg => cfg.AddCommands(new Tool())).Run(commandLine);

    private const char Esc = '';        // ESC — begins every ANSI escape sequence
    private const char ZeroWidth = '​';  // zero-width space

    // ESC-prefixed ANSI colour codes wrapping visible text — the classic terminal-rewrite payload.
    private static readonly string Ansi = $"{Esc}[31mabc{Esc}[0m";

    [Fact]
    public void Strip_Ansi_Escapes_From_An_Unconvertible_Argument()
    {
        var result = Run($"app square {Ansi}");

        result.ExpectExit(2);
        Assert.DoesNotContain(Esc, result.StandardError);
        // Sanitizes, does not swallow: the visible text still reaches the user.
        Assert.Contains("[31mabc[0m", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void Strip_ZeroWidth_Codepoints_From_An_Unconvertible_Argument()
    {
        // A zero-width space between two digits: invisible to a human, still an unconvertible int.
        var result = Run($"app square 1{ZeroWidth}2");

        result.ExpectExit(2);
        Assert.DoesNotContain(ZeroWidth, result.StandardError);
    }

    [Fact]
    public void Strip_Ansi_Escapes_From_A_Missing_Argument_Message()
    {
        // The argument name is developer-supplied and clean, but the message still routes through the
        // same choke point — assert it emits no control characters.
        var result = Run("app square");

        result.ExpectExit(2);
        Assert.DoesNotContain(Esc, result.StandardError);
    }

    [Fact]
    public void Sanitize_Argument_Errors_The_Same_Way_As_Option_Errors()
    {
        // Parity: the identical malicious token, once through an argument and once through an option.
        var argumentError = Run($"app square {Ansi}");
        var optionError = Run($"app scale --by {Ansi}");

        argumentError.ExpectExit(2);
        optionError.ExpectExit(2);
        Assert.DoesNotContain(Esc, argumentError.StandardError);
        Assert.DoesNotContain(Esc, optionError.StandardError);
    }
}

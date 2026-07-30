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
        [CliRoute("square {n}")]
        [CliCommandExample("square 5")]
        int Square([CliArgument("the number to square")] int n);

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

    /// <summary>
    /// The Trojan Source set (CVE-2021-42574) is nine bidi controls, and the sanitizer used to strip
    /// five of them: U+202A-U+202E, the embeddings and overrides Unicode 6.3 *deprecated*. The four
    /// isolates that replaced them — and the Arabic letter mark — reached stderr verbatim, which is
    /// the spelling an attacker reaching for the attack today would actually use. Verified by
    /// running each through the real pipeline before the fix, not reasoned from the source.
    /// </summary>
    [Theory]
    [InlineData(0x061C, "ARABIC LETTER MARK")]
    [InlineData(0x202A, "LEFT-TO-RIGHT EMBEDDING")]
    [InlineData(0x202B, "RIGHT-TO-LEFT EMBEDDING")]
    [InlineData(0x202C, "POP DIRECTIONAL FORMATTING")]
    [InlineData(0x202D, "LEFT-TO-RIGHT OVERRIDE")]
    [InlineData(0x202E, "RIGHT-TO-LEFT OVERRIDE")]
    [InlineData(0x2066, "LEFT-TO-RIGHT ISOLATE")]
    [InlineData(0x2067, "RIGHT-TO-LEFT ISOLATE")]
    [InlineData(0x2068, "FIRST STRONG ISOLATE")]
    [InlineData(0x2069, "POP DIRECTIONAL ISOLATE")]
    public void Strip_Every_Bidi_Control_From_An_Unconvertible_Argument(int codepoint, string name)
    {
        var control = (char)codepoint;

        var result = Run($"app square 1{control}2");

        result.ExpectExit(2);
        Assert.DoesNotContain(
            control,
            result.StandardError);
        Assert.DoesNotContain(
            control,
            result.StandardOut);

        // Named in the message so a failure reads as the codepoint that leaked, not "a char".
        Assert.True(
            !result.StandardError.Contains(control),
            $"U+{codepoint:X4} {name} reached stderr. Reordering controls make rendered text disagree " +
            "with the bytes behind it (CVE-2021-42574); the whole family must be stripped, not half.");
    }

    /// <summary>
    /// The unhandled-exception path composes "Unhandled error: {message}" and used to write the
    /// message raw. An exception message routinely carries argv — a bound <c>--path</c> lands
    /// verbatim inside a <c>FileNotFoundException</c> — so this was the same terminal-rewrite and
    /// prompt-injection channel the option and argument paths already closed, reachable from any
    /// handler that lets a BCL exception escape.
    /// </summary>
    [Fact]
    public void Strip_Control_Characters_From_An_Unhandled_Exception_Message()
    {
        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands(new Exploder()))
            .Run("app explode");

        result.ExpectExit(1);
        Assert.DoesNotContain(Esc, result.StandardError);
        Assert.DoesNotContain(ZeroWidth, result.StandardError);

        // Sanitizes, does not swallow: the diagnosis still reaches the user.
        Assert.Contains("Unhandled error:", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("[31mdisk on fire[0m", result.StandardError, StringComparison.Ordinal);
    }

    public interface IExploder
    {
        [CliRoute("explode")]
        [CliCommandExample("explode")]
        int Explode();
    }

    private sealed class Exploder : IExploder
    {
        // Stands in for the realistic case: a BCL exception whose message embeds an argv-derived
        // value the framework then echoes.
        public int Explode() =>
            throw new InvalidOperationException($"{Esc}[31mdisk on fire{Esc}[0m{ZeroWidth}");
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

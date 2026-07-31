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

    // --- POR-160: the policy, not another incident ---------------------------------------------
    //
    // The blocklist used to grow one attack at a time — zero-widths, then the bidi family — so the
    // answer to "is X covered?" was whoever had last been attacked. The rule is now: nothing
    // survives that a reader cannot see. Implemented as UnicodeCategory.Format (the BCL's own data,
    // which already contains the tag block) plus a short list of invisible codepoints that are not
    // Cf, each verified against Rune.GetUnicodeCategory rather than assumed.

    /// <summary>
    /// The invisible codepoints that reached stderr verbatim when this ticket was written, plus the
    /// classes named in it that were never probed.
    /// </summary>
    [Theory]
    [InlineData(0x00AD, "SOFT HYPHEN")]
    [InlineData(0x034F, "COMBINING GRAPHEME JOINER")]
    [InlineData(0x115F, "HANGUL CHOSEONG FILLER")]
    [InlineData(0x3164, "HANGUL FILLER")]
    [InlineData(0x180E, "MONGOLIAN VOWEL SEPARATOR")]
    [InlineData(0x180B, "MONGOLIAN FREE VARIATION SELECTOR ONE")]
    [InlineData(0x206A, "INHIBIT SYMMETRIC SWAPPING")]
    [InlineData(0x206F, "NOMINAL DIGIT SHAPES")]
    [InlineData(0x2065, "reserved default-ignorable")]
    [InlineData(0xFE0F, "VARIATION SELECTOR-16")]
    [InlineData(0xFFA0, "HALFWIDTH HANGUL FILLER")]
    [InlineData(0xFFF0, "reserved default-ignorable")]
    public void Strip_An_Invisible_Codepoint_From_An_Unconvertible_Argument(int codepoint, string name)
    {
        var invisible = char.ConvertFromUtf32(codepoint);

        var result = Run($"app square 1{invisible}2");

        result.ExpectExit(2);
        Assert.True(
            !result.StandardError.Contains(invisible, StringComparison.Ordinal),
            $"U+{codepoint:X4} {name} reached stderr. Text nobody can see is still text in an " +
            "agent's context window.");
    }

    /// <summary>
    /// The tag block — the "invisible instructions" vector, where readable ASCII is encoded in
    /// codepoints renderers drop and models read.
    /// </summary>
    /// <remarks>
    /// <b>This is the case that forced rune iteration.</b> U+E0000–U+E007F is outside the BMP, so it
    /// arrives as a surrogate pair; the old char-by-char loop met two chars in the 0xD800–0xDFFF
    /// range, neither of which any rule named, and could not have expressed the codepoint at all.
    /// The payload below spells "HI" in tag characters, which is exactly the shape of the attack.
    /// </remarks>
    [Fact]
    public void Strip_The_Tag_Block_Which_Needs_A_Surrogate_Pair_To_Express()
    {
        var tagged = char.ConvertFromUtf32(0xE0048) + char.ConvertFromUtf32(0xE0049);   // tag "HI"

        Assert.Equal(4, tagged.Length);          // two surrogate pairs — a char loop sees four chars

        var result = Run($"app square 1{tagged}2");

        result.ExpectExit(2);
        Assert.DoesNotContain(tagged, result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("\uDB40", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// The near-miss that decided the implementation: a variation selector is <c>Mn</c>, and so is
    /// every combining accent. A category rule for non-spacing marks would have stripped the accent
    /// out of a user's own argument and quietly changed what the diagnostic said they typed.
    /// </summary>
    [Theory]
    [InlineData("café")]           // precomposed é
    [InlineData("café")]     // e + COMBINING ACUTE ACCENT
    [InlineData("naïve")]
    [InlineData("日本語")]
    [InlineData("Ω")]
    public void Keep_Every_Visible_Character_Including_Combining_Marks(string visible)
    {
        var result = Run($"app square {visible}");

        result.ExpectExit(2);
        Assert.Contains(visible, result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything the enumerated rules blocked before POR-160 is still blocked. The change replaced
    /// hand-listed ranges with a category test, and a regression there would be silent — the message
    /// would simply carry the character again.
    /// </summary>
    [Theory]
    [InlineData(0x200B)]
    [InlineData(0x200C)]
    [InlineData(0x200D)]
    [InlineData(0x200E)]
    [InlineData(0x200F)]
    [InlineData(0x2060)]
    [InlineData(0xFEFF)]
    [InlineData(0x061C)]
    [InlineData(0x202A)]
    [InlineData(0x202E)]
    [InlineData(0x2066)]
    [InlineData(0x2069)]
    public void Keep_Blocking_Everything_The_Enumerated_Rules_Blocked(int codepoint)
    {
        var result = Run($"app square 1{(char)codepoint}2");

        result.ExpectExit(2);
        Assert.DoesNotContain((char)codepoint, result.StandardError);
    }

    /// <summary>
    /// An unpaired surrogate is malformed and cannot render; it is dropped rather than replaced, so
    /// no character the user did not type appears in the message.
    /// </summary>
    [Fact]
    public void Drop_An_Unpaired_Surrogate_Without_Substituting_One()
    {
        var result = Run("app square 1\uD83D2");

        result.ExpectExit(2);
        Assert.DoesNotContain('\uD83D', result.StandardError);
        Assert.DoesNotContain('�', result.StandardError);
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

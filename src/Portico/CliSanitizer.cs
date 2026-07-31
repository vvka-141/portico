using System;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace Portico;

/// <summary>
/// Strips control characters and invisible codepoints from text the framework echoes back — the
/// command line the user typed, an option value that failed to convert, a route it could not find.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Everything the framework echoes is <em>attacker-influenced input</em>: it
/// came from argv. Left raw, a crafted command line can carry ANSI escape sequences that rewrite the
/// terminal, or zero-width characters that hide text — and, increasingly, that output is read by an
/// agent rather than a human. Unsanitized stderr is a prompt-injection channel: text nobody can see
/// in a terminal is text a model still reads.
/// </para>
/// <para>
/// <b>What it does NOT touch: handler output.</b> A handler writes with <c>Console.Write*</c> and owns
/// what it emits — the handler contract is sacred (CHARTER §4), and a framework that filtered a
/// handler's bytes would break every program that deliberately emits colour. This applies only to the
/// strings the <em>framework itself</em> composes out of user input.
/// </para>
/// </remarks>
internal static class CliSanitizer
{
    /// <summary>
    /// Returns <paramref name="text"/> with control characters (including the ESC that begins every
    /// ANSI sequence) and zero-width codepoints removed. Tab and newline survive: they are legitimate
    /// layout in a multi-line diagnostic, and neither hides text nor moves the cursor.
    /// </summary>
    /// <remarks>
    /// Iterates <b>runes</b>, not chars. The tag block (U+E0020–U+E007F) is the "invisible
    /// instructions" injection vector — ASCII encoded in codepoints most renderers drop and every
    /// model reads — and it lives outside the BMP, so a char-by-char loop met it as two surrogates
    /// that individually look like ordinary characters and could not express it at all (POR-160).
    /// <para>
    /// A malformed sequence — an unpaired surrogate — is dropped rather than replaced. It came from
    /// argv, it cannot render, and substituting U+FFFD would put a character in the message that the
    /// user did not type.
    /// </para>
    /// </remarks>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        StringBuilder? builder = null;
        var index = 0;

        while (index < text!.Length)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out var consumed);

            if (status == OperationStatus.Done && IsAllowed(rune))
            {
                builder?.Append(text.AsSpan(index, consumed));
            }
            else
            {
                // First offender: copy everything before it, then drop it and every later one.
                builder ??= new StringBuilder(text.Length).Append(text, 0, index);
            }

            index += consumed;
        }

        return builder?.ToString() ?? text;
    }

    /// <summary>
    /// <b>The policy: nothing survives that a reader cannot see.</b> A diagnostic is text a human
    /// reads or a model ingests, so a codepoint that renders as nothing has no business in one —
    /// whatever its script or intent.
    /// </summary>
    /// <remarks>
    /// Enumerated rules used to accumulate one incident at a time — zero-widths, then the bidi
    /// family — which meant the answer to "is X covered?" was whoever had last been attacked. This
    /// is the rule instead, and POR-160 is where it was written down.
    /// <para>
    /// <b>Every format character, by category, not by table.</b> <see cref="UnicodeCategory.Format"/>
    /// is the BCL's own Unicode data and it already contains the codepoint that motivated this
    /// ticket: the tag block is <c>Cf</c>, so the injection vector is covered by asking the runtime
    /// rather than by hard-coding a range list this repository would then own and have to age. It
    /// also covers, for free, every zero-width and bidi control the enumerated rules listed by hand.
    /// </para>
    /// <para>
    /// <b>Why not Unicode's Default_Ignorable_Code_Point,</b> which is the property that most exactly
    /// means "renderers may show nothing here". It is the right idea and the wrong mechanism: the BCL
    /// does not expose it, so using it means transcribing ~18 ranges from the UCD into this file and
    /// re-verifying them at every Unicode revision — a table nobody would re-check, guarding a
    /// security boundary. <c>Cf</c> plus the short list below reaches the same codepoints for the
    /// threats that exist, and the parts it over-covers (Arabic number signs, interlinear annotation)
    /// are format characters too, which have no place in a diagnostic either.
    /// </para>
    /// <para>
    /// <b>The list below is only what is invisible but NOT <c>Cf</c></b>, each verified against
    /// <see cref="Rune.GetUnicodeCategory"/> rather than assumed. Note what is absent: no blanket
    /// category rule for non-spacing marks. Variation selectors are <c>Mn</c> — and so is every
    /// combining accent, so stripping the category would corrupt <c>café</c> into <c>cafe</c>. That
    /// near-miss is the reason these are enumerated.
    /// </para>
    /// <para>
    /// <b>What this costs, so it is not rediscovered as a bug.</b> A variation selector is invisible
    /// by construction, so U+FE0F stops an emoji rendering in its colour form, and U+E0100+ stops a
    /// CJK ideograph selecting a glyph variant. Both degrade inside <i>error messages only</i> —
    /// never handler output, which this class does not touch — and a diagnostic is the last place
    /// typographic fidelity outranks not carrying an invisible payload.
    /// </para>
    /// </remarks>
    private static bool IsAllowed(Rune rune)
    {
        var code = rune.Value;

        // Tab and newline are legitimate layout in a multi-line diagnostic. Neither hides text nor
        // moves the cursor, so both survive.
        if (code is 0x09 or 0x0A) return true;

        // C0 (0x00-0x1F) includes ESC (0x1B), which begins every ANSI escape sequence; then DEL
        // (0x7F) and the C1 block (0x80-0x9F).
        if (code < 0x20 || code == 0x7F || (code >= 0x80 && code <= 0x9F)) return false;

        // Zero-widths, joiners, the BOM, the whole bidi control family — including the isolates that
        // Unicode 6.3 introduced as the replacement for the deprecated overrides, i.e. the spelling
        // a Trojan Source attack (CVE-2021-42574) would actually use — and the tag block. All Cf.
        if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) return false;

        return !IsInvisibleNonFormat(code);
    }

    /// <summary>Invisible codepoints the <c>Cf</c> category does not reach.</summary>
    private static bool IsInvisibleNonFormat(int code) => code switch
    {
        0x034F => true,                                     // COMBINING GRAPHEME JOINER (Mn)
        0x115F or 0x1160 or 0x3164 or 0xFFA0 => true,       // Hangul fillers — blank, but Lo
        >= 0x17B4 and <= 0x17B5 => true,                    // KHMER VOWEL INHERENT AQ/AA (Mn)
        >= 0x180B and <= 0x180F => true,                    // Mongolian variation selectors (Mn)
        0x2065 => true,                                     // unassigned, reserved default-ignorable
        >= 0xFE00 and <= 0xFE0F => true,                    // VARIATION SELECTOR-1..16 (Mn)
        >= 0xFFF0 and <= 0xFFF8 => true,                    // unassigned, reserved default-ignorable
        >= 0xE0100 and <= 0xE01EF => true,                  // VARIATION SELECTOR SUPPLEMENT (Mn)
        _ => false,
    };
}

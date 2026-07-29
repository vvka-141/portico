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
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        StringBuilder? builder = null;
        for (var i = 0; i < text!.Length; i++)
        {
            var c = text[i];
            if (IsAllowed(c))
            {
                builder?.Append(c);
                continue;
            }

            // First offender: copy everything before it, then drop it and every later one.
            builder ??= new StringBuilder(text.Length).Append(text, 0, i);
        }

        return builder?.ToString() ?? text;
    }

    private static bool IsAllowed(char c)
    {
        var code = (int)c;

        // Tab and newline are legitimate layout in a multi-line diagnostic. Neither hides text nor
        // moves the cursor, so both survive.
        if (code is 0x09 or 0x0A) return true;

        // C0 (0x00-0x1F) includes ESC (0x1B), which begins every ANSI escape sequence; then DEL
        // (0x7F) and the C1 block (0x80-0x9F).
        if (code < 0x20 || code == 0x7F || (code >= 0x80 && code <= 0x9F)) return false;

        // Invisible to a human, read by a model: zero-width space/joiners and the LTR/RTL marks
        // (U+200B-U+200F), the word joiner (U+2060) and the BOM (U+FEFF). Text nobody can see is
        // still text in an agent's context window.
        if (code >= 0x200B && code <= 0x200F) return false;
        if (code == 0x2060 || code == 0xFEFF) return false;

        // The bidi control family, in full. These reorder rendered text without changing a byte, so
        // a crafted argv token can make stderr display something other than what it says — the
        // Trojan Source attack (CVE-2021-42574). The set is deliberately all nine plus the Arabic
        // letter mark: this check used to cover only U+202A-U+202E, which are the *deprecated*
        // embeddings and overrides, and let the isolates through. Unicode 6.3 introduced the
        // isolates as the recommended replacement for exactly those codepoints, so blocking one
        // half and not the other blocked the spelling an attacker would not use.
        if (code == 0x061C) return false;                    // ALM
        if (code >= 0x202A && code <= 0x202E) return false;   // LRE RLE PDF LRO RLO
        if (code >= 0x2066 && code <= 0x2069) return false;   // LRI RLI FSI PDI

        return true;
    }
}

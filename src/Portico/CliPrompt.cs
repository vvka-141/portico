using System;
using System.Text;

namespace Portico;

/// <summary>
/// Interactive prompt helpers. Methods route through <see cref="ICliConsole"/> so tests and
/// hosted scenarios that inject a custom console see prompts on the same streams the rest of
/// the framework writes to. Under <see cref="Portico.Testing.CliTestHarness"/>,
/// pass <c>input:</c> to the <c>Run</c> call to feed answers.
/// </summary>
public static class CliPrompt
{
    // ---------------------------------------------------------------------------------------
    //  Yes / no
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Prompts with a yes/no question and returns the answer as a boolean. Repeats the prompt
    /// until the user enters a line whose first non-whitespace character is <c>Y</c> or
    /// <c>N</c> (case-insensitive).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="ICliConsole.In"/> returns end-of-stream before a valid answer
    /// is received — typically when input has been redirected to an empty file or stream.
    /// </exception>
    public static bool GetYesNoAnswer(ICliConsole console, string message)
    {
        ThrowIf.ArgumentNull(console);
        ThrowIf.ArgumentNullOrWhiteSpace(message);

        while (true)
        {
            console.Out.WriteLine($"{message} (Y/N)");
            var line = ReadRequiredLine(console);
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            var first = char.ToUpperInvariant(trimmed[0]);
            if (first == 'Y') return true;
            if (first == 'N') return false;
            console.Out.WriteLine("Invalid input. Please type 'Y' or 'N'.");
        }
    }

    /// <summary>Convenience overload that targets <see cref="SystemCliConsole.Instance"/>.</summary>
    public static bool GetYesNoAnswer(string message) =>
        GetYesNoAnswer(SystemCliConsole.Instance, message);

    // ---------------------------------------------------------------------------------------
    //  Line input with optional default
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reads one line from the user. If <paramref name="defaultValue"/> is supplied and the
    /// user submits an empty line, the default is returned; otherwise the user's input is
    /// returned verbatim (trimmed of trailing whitespace).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when input ends before any line is received <b>and</b> no default was supplied.
    /// </exception>
    public static string GetLine(ICliConsole console, string message, string? defaultValue = null)
    {
        ThrowIf.ArgumentNull(console);
        ThrowIf.ArgumentNullOrWhiteSpace(message);

        var prompt = defaultValue is null ? $"{message}: " : $"{message} [{defaultValue}]: ";
        console.Out.Write(prompt);
        var line = console.In.ReadLine();
        if (line is null)
        {
            if (defaultValue is not null) return defaultValue;
            throw EndOfInputException(nameof(GetLine));
        }
        var trimmed = line.TrimEnd();
        if (trimmed.Length == 0 && defaultValue is not null) return defaultValue;
        return trimmed;
    }

    /// <summary>Convenience overload that targets <see cref="SystemCliConsole.Instance"/>.</summary>
    public static string GetLine(string message, string? defaultValue = null) =>
        GetLine(SystemCliConsole.Instance, message, defaultValue);

    // ---------------------------------------------------------------------------------------
    //  Password (no echo)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a password with no echo to the console. On a real terminal, uses
    /// <see cref="Console.ReadKey(bool)"/> character-by-character so typed characters aren't
    /// displayed. On a redirected stream (tests, piped input), falls back to
    /// <see cref="ICliConsole.In"/>.<c>ReadLine()</c> — the caller supplies the password
    /// through the redirected stream just like any other input. Echoes a <c>*</c> per typed
    /// character on a real terminal so the user sees they're typing.
    /// </summary>
    /// <remarks>
    /// The returned string is a managed <see cref="string"/>, not a <see cref="System.Security.SecureString"/>.
    /// For high-security scenarios that require zeroing memory after use, read with this
    /// method and immediately wrap the value in your own `SecureString` or secret-vault type.
    /// The framework's job is UX plumbing, not key management.
    /// </remarks>
    public static string GetPassword(ICliConsole console, string message)
    {
        ThrowIf.ArgumentNull(console);
        ThrowIf.ArgumentNullOrWhiteSpace(message);

        console.Out.Write($"{message}: ");

        // Redirected input path (test harness, piped stdin) — no ReadKey available.
        if (console.IsOutputRedirected || Console.IsInputRedirected)
        {
            var line = console.In.ReadLine()
                ?? throw EndOfInputException(nameof(GetPassword));
            return line.TrimEnd();
        }

        // Interactive terminal path — read chars without echo, show '*' for each keystroke,
        // honor Backspace.
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                console.Out.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    console.Out.Write("\b \b");
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                console.Out.Write('*');
            }
        }
    }

    /// <summary>Convenience overload that targets <see cref="SystemCliConsole.Instance"/>.</summary>
    public static string GetPassword(string message) =>
        GetPassword(SystemCliConsole.Instance, message);

    // ---------------------------------------------------------------------------------------
    //  Destructive-op guard — confirm by typing the exact word
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Prompts the user to type <paramref name="expected"/> verbatim — matched
    /// case-sensitively — before returning <c>true</c>. Any other input returns
    /// <c>false</c>. Use for destructive / irreversible operations where a Y/N prompt is
    /// too easy to hit by muscle memory (drop database, force-push main, delete tenant).
    /// </summary>
    /// <example>
    /// <code>
    /// if (!CliPrompt.ConfirmByTyping($"This will DROP the '{db}' database.", "DELETE"))
    /// {
    ///     Console.Error.WriteLine("Cancelled.");
    ///     return 1;
    /// }
    /// </code>
    /// </example>
    public static bool ConfirmByTyping(ICliConsole console, string message, string expected)
    {
        ThrowIf.ArgumentNull(console);
        ThrowIf.ArgumentNullOrWhiteSpace(message);
        ThrowIf.ArgumentNullOrWhiteSpace(expected);

        console.Out.WriteLine(message);
        console.Out.Write($"Type '{expected}' to confirm: ");
        var line = console.In.ReadLine();
        if (line is null) return false;
        return string.Equals(line.Trim(), expected, StringComparison.Ordinal);
    }

    /// <summary>Convenience overload that targets <see cref="SystemCliConsole.Instance"/>.</summary>
    public static bool ConfirmByTyping(string message, string expected) =>
        ConfirmByTyping(SystemCliConsole.Instance, message, expected);

    // ---------------------------------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------------------------------

    private static string ReadRequiredLine(ICliConsole console)
    {
        var line = console.In.ReadLine();
        if (line is null) throw EndOfInputException(nameof(GetYesNoAnswer));
        return line;
    }

    private static InvalidOperationException EndOfInputException(string op) =>
        new($"{op}: the input stream returned end-of-stream before a valid answer was received. " +
            "If running non-interactively, supply the answer via the input stream or use a non-prompting code path.");
}

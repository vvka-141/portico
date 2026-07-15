using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;



namespace Portico;

/// <summary>
/// The parsed, typed representation of a CLI invocation — what the user typed, decoded into
/// <see cref="ExecutableName"/>, <see cref="Segments"/> (positional tokens), and
/// <see cref="Options"/> (typed captures: flag / scalar / collection / keyed). Produced once
/// per <see cref="CliApplication.Run(string)"/> call and forwarded to every lifecycle hook.
/// </summary>
/// <remarks>
/// Instances are immutable and own both the shell-style tokenization (quotes, whitespace,
/// POSIX <c>--</c> terminator, negative-number guard) and the structural parse into option
/// captures. Route matching and handler binding happen downstream in <see cref="CliApplication"/>;
/// this type's job ends when the parse is done.
/// </remarks>
[DebuggerDisplay(@"{ToString(""D"")}")]
public sealed partial class CliInvocation : IFormattable
{
    // Source-generated regexes. Single-line quoted-literal check, option-tokenizer, bracket
    // rewrite, and key extractor — all on the hot path of every Run() call. Source-gen
    // emits a compiled, AOT-friendly Regex subclass at build time.
    [GeneratedRegex(@"^""[^""]*""$", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedOnlyRegex();

    // A token is a run of quoted spans and unquoted non-space chars, GLUED TOGETHER: `--name="two
    // words"` is ONE token, not `--name="two` followed by `words"`. The previous pattern alternated
    // between a quoted span and \S+, so a quote that began mid-token was never seen as a quote and
    // the value was torn in half at the space (POR-56).
    [GeneratedRegex(@"(?:(?<!\\)""[^""]*(?<!\\)""|[^\s""]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex ArgTokenizerRegex();

    // The key group allows an empty match (`[]`, `[^\[\]]*`) so that the malformed `--config[]`
    // form is rewritten to a keyed capture with an empty key — the dictionary materializer then
    // rejects it with a targeted "empty map key" message instead of leaking an "unrecognized
    // option" (SOL-76). Non-empty keys are unaffected.
    [GeneratedRegex(@"\[([^\[\]]*)\]$", RegexOptions.CultureInvariant)]
    private static partial Regex BracketSuffixRegex();

    [GeneratedRegex(@"(?<option>\S+?)\.(?<key>[^\.]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyedOptionRegex();

    /// <summary>
    /// Parses the specified command line string and returns a <see cref="CliInvocation"/> instance representing its components.
    /// </summary>
    /// <param name="commandLine">The raw command line string to parse.</param>
    /// <returns>
    /// A <see cref="CliInvocation"/> object that contains the executable name, arguments, and options extracted from the provided command line.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when the <paramref name="commandLine"/> is <c>null</c>, empty, or consists solely of white-space characters.
    /// </exception>
    /// <remarks>
    /// The <see cref="FromArgs(string)"/> method processes the input command line string through a series of transformation steps to extract the executable name,
    /// environment variables, quoted strings, keyed options, and other options. It ensures that the command line adheres to the expected format
    /// and encodes specific components to maintain consistency and avoid conflicts during parsing.
    /// 
    /// This method is marked with the <see cref="DebuggerStepThroughAttribute"/> attribute to instruct the debugger to step through the method without
    /// stopping, providing a smoother debugging experience by avoiding stepping into boilerplate code.
    /// </remarks>
    /// <example><code>
    /// var invocation = CliInvocation.FromArgs("git commit -m \"initial\" --amend");
    /// Console.WriteLine(invocation.ExecutableName); // "git"
    /// </code></example>
    [DebuggerStepThrough]
    public static CliInvocation FromArgs(string commandLine) =>
        new(TokenizeFromString(commandLine));

    /// <summary>
    /// Shell-style tokenization of a command-line string to an argv-style array. Public
    /// factory: <see cref="FromArgs(string)"/>. Exposed to the application so it can expand
    /// POSIX short-option combinations (<c>-abc</c>, <c>-n5</c>) before the option-capture pass
    /// runs — see <see cref="CliShortOptionExpander"/>.
    /// </summary>
    internal static string[] TokenizeFromString(string commandLine)
    {
        commandLine = ThrowIf
            .ArgumentNullOrWhiteSpace(commandLine)
            .Trim();
        if (QuotedOnlyRegex().IsMatch(commandLine))
        {
            return [commandLine.Trim('"')];
        }
        return SplitCommandLine(commandLine).ToArray();
    }


    /// <example><code>
    /// // Parse the current process command line (argv[0] is the executable path).
    /// var invocation = CliInvocation.FromArgs();
    /// </code></example>
    [DebuggerStepThrough]
    public static CliInvocation FromArgs() => new(ProcessArgv());

    /// <summary>
    /// The current process's argv, with argv[0] replaced by the name the user actually typed.
    /// <para>
    /// <see cref="Environment.GetCommandLineArgs"/>[0] is the path to the managed <b>.dll</b> for an
    /// apphost-launched app, so echoing it back prints <c>myapp.dll</c> — a name the user never
    /// typed and cannot run. <see cref="Environment.ProcessPath"/> is the apphost itself
    /// (<c>myapp.exe</c>); stripping the extension yields <c>myapp</c>, which is what every other
    /// CLI prints and what the user can paste back into their shell.
    /// </para>
    /// <para>
    /// This applies ONLY to the process-derived path. A caller who supplies argv explicitly —
    /// <c>CliTestHarness.Run("app.exe echo world")</c>, or <see cref="FromArgs(string[])"/> — keeps
    /// the name they gave, verbatim. Their argv is not ours to reinterpret.
    /// </para>
    /// </summary>
    internal static string[] ProcessArgv()
    {
        var argv = Environment.GetCommandLineArgs();
        if (argv.Length == 0)
        {
            return [ProcessExecutableName()];
        }

        var copy = (string[])argv.Clone();
        copy[0] = ProcessExecutableName();
        return copy;
    }

    /// <summary>
    /// The display name of the running executable, without a directory or a file extension.
    /// </summary>
    internal static string ProcessExecutableName()
    {
        // ProcessPath is null under some hosting shapes; fall back to argv[0], which at least
        // names the right assembly even if it carries a .dll extension.
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var argv = Environment.GetCommandLineArgs();
            path = argv.Length > 0 ? argv[0] : null;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return "app";
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? "app" : name;
    }

    /// <summary>
    /// Parses a pre-tokenized argv-style array. Prefer this over <see cref="FromArgs(string)"/>
    /// for programmatic callers — it bypasses string-level tokenization and preserves argument
    /// boundaries exactly as the host shell already resolved them.
    /// </summary>
    /// <example><code>
    /// var invocation = CliInvocation.FromArgs(new[] { "myapp", "deploy", "--env", "prod" });
    /// Console.WriteLine(invocation.Segments[0]); // "deploy"
    /// </code></example>
    [DebuggerStepThrough]
    public static CliInvocation FromArgs(string[] args)
    {
        ThrowIf.ArgumentNull(args);
        if (args.Length == 0)
        {
            throw new ArgumentException("Expected at least one element (the executable name).", nameof(args));
        }
        return new CliInvocation(args);
    }

    private CliInvocation(string[] args)
    {
        // `--name=value` is split HERE, not in the string tokenizer, because argv is the path a real
        // shell takes: by the time a process starts, the shell has already removed the quotes and
        // handed us one glued token. Splitting only in TokenizeFromString meant the GNU long form —
        // `git --git-dir=/x`, `docker --filter=name=foo`, what users actually type — worked in
        // Run(string) and failed for every real invocation (POR-56). A token with no separator, and
        // any token that is not an option, passes through untouched, so this is safe to run over a
        // command line the tokenizer already split.
        var queue = new Queue<string>(ExpandOptionAssignments(args));
        ExecutableName = Path.GetFileName(
            queue.Dequeue()
                .Trim('"')
                .Replace('\\', Path.DirectorySeparatorChar))!;

        var segments = queue
            .DequeueWhile(arg => !IsOptionToken(arg))
            .Select(arg => arg.Trim('"'))
            .ToList();

        var options = new List<CliOptionCapture>();
        var values = new List<string>();
        while (queue.Any())
        {
            var optionName = queue.Peek();
            if (optionName == EndOfOptionsToken)
            {
                // POSIX terminator: everything after `--` is positional, regardless of dashes.
                queue.Dequeue();
                break;
            }
            queue.Dequeue();
            optionName = BracketSuffixRegex().Replace(optionName, m => $@".{m.Groups[1]}");
            values.Clear();
            values.AddRange(queue
                .DequeueWhile(arg => !IsOptionToken(arg) && arg != EndOfOptionsToken)
                .Select(v => v.Trim('"')));
            var match = KeyedOptionRegex().Match(optionName);
            if (match.Success)
            {
                optionName = match.Groups["option"].Value;
                var key = match.Groups["key"].Value;
                if (values.Count == 0)
                {
                    options.Add(new CliKeyFlagOptionCapture(optionName, key));
                }
                else if (values.Count == 1)
                {
                    options.Add(new CliKeyValueOptionCapture(optionName, key, values.Single()));
                }
                else
                {
                    options.Add(new CliKeyCollectionOptionCapture(optionName, key, [.. values]));
                }
            }
            else if(values.Count == 0)
            {
                options.Add(new CliFlagOptionCapture(optionName));
            }
            else if (values.Count == 1)
            {
                options.Add(new CliScalarOptionCapture(optionName, values.Single()));
            }
            else
            {
                options.Add(new CliCollectionOptionCapture(optionName, [.. values]));
            }
        }

        // Anything remaining after the `--` terminator is positional.
        while (queue.Count > 0)
        {
            segments.Add(queue.Dequeue().Trim('"'));
        }

        Segments = [.. segments];
        Options = [.. options];
    }

    private const string EndOfOptionsToken = "--";

    /// <summary>
    /// Gets the name of the executable extracted from the command line.
    /// </summary>
    /// <value>
    /// A <see cref="string"/> representing the executable name.
    /// </value>
    public string ExecutableName { get; }

    /// <summary>
    /// Gets a collection of parsed options extracted from the command line.
    /// </summary>
    /// <value>
    /// An <see cref="ImmutableArray{T}"/> of <see cref="CliOptionCapture"/> representing the options
    /// extracted from the command line. These options can include flags, key-value pairs, or collections of values.
    /// </value>
    public ImmutableArray<CliOptionCapture> Options { get; }

    /// <summary>
    /// Gets a collection of subcommands or arguments captured from the command line.
    /// </summary>
    /// <value>
    /// An <see cref="ImmutableArray{T}"/> of <see cref="string"/> representing the segments
    /// (subcommands or arguments) extracted from the command line.
    /// </value>
    /// <remarks>
    /// The <see cref="Segments"/> property contains the subcommands or arguments that follow the executable name.
    /// These segments provide additional context or instructions for the command being executed.
    /// </remarks>
    public ImmutableArray<string> Segments { get; }

    /// <summary>
    /// Capture names whose values must never be rendered. Populated at dispatch from the matched
    /// route's <c>[CliOption(Sensitive = true)]</c> members — which is why it can only ever be
    /// filled once a route HAS matched. When no route matches, the option metadata does not exist
    /// and the framework cannot know what is secret; the unknown-command path therefore renders no
    /// option values at all rather than guessing.
    /// </summary>
    private readonly HashSet<string> _redactedOptionNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The placeholder rendered in place of a sensitive option's value.</summary>
    internal const string Redacted = "***";

    /// <summary>
    /// Marks every capture matching a sensitive option member so <see cref="ToString(string?, IFormatProvider?)"/>
    /// renders <c>***</c> in place of its value.
    /// </summary>
    internal void RedactSensitiveOptions(IEnumerable<Reflection.ICliOptionMemberInfo> options)
    {
        foreach (var option in options)
        {
            if (!option.IsSensitive) continue;
            foreach (var capture in Options)
            {
                if (option.IsMatch(capture.Name))
                {
                    _redactedOptionNames.Add(capture.Name);
                }
            }
        }
    }

    private bool IsRedacted(string optionName) => _redactedOptionNames.Contains(optionName);

    /// <summary>
    /// True iff the token should be treated as the start of a new option (and not as a value of
    /// the previous one). A leading dash is the basic signal — but a token like <c>-5</c>,
    /// <c>-1.5</c>, <c>-.5</c> is a negative number and must be passed through as a value, not
    /// mistaken for a short option named "5". Only single-dash short forms get the numeric
    /// override; <c>--5</c> would still be treated as an option (and rightly rejected
    /// downstream since it's not a valid name).
    /// </summary>
    internal static bool IsOptionToken(string token)
    {
        if (token.Length < 2 || token[0] != '-') return false;
        if (token[1] == '-') return true;       // long form: --foo
        // Short-form lookahead: -<digit> or -.<digit> is a negative number.
        if (char.IsDigit(token[1])) return false;
        if (token.Length >= 3 && token[1] == '.' && char.IsDigit(token[2])) return false;
        return true;
    }

    internal static IEnumerable<string> SplitCommandLine(string commandLine)
    {
        // Tokenizes whitespace-separated args, preserving double-quoted segments. No environment-
        // variable expansion: the host shell handles %VAR% (cmd.exe) / $VAR (POSIX) before a process
        // ever receives argv, and re-expanding here leads to platform-specific surprises.
        //
        // `--name=value` is NOT split here (POR-56). It is split in the constructor, which every path
        // goes through — including argv, which this one does not touch. One choke point, and it is
        // the one that sees a real shell's arguments.
        for (Match match = ArgTokenizerRegex().Match(commandLine); match.Success; match = match.NextMatch())
        {
            yield return match.Value;
        }
    }

    /// <summary>
    /// Splits every <c>--name=value</c> / <c>-n:value</c> token into its two parts, so the option
    /// parser sees the same shape it would from a space-separated form.
    /// </summary>
    /// <remarks>
    /// Stops at the POSIX <c>--</c> terminator: after it, <c>--name=x</c> is a positional argument
    /// that merely looks like an option, and rewriting it would corrupt the very thing the terminator
    /// exists to protect.
    /// </remarks>
    private static string[] ExpandOptionAssignments(string[] args)
    {
        var expanded = new List<string>(args.Length + 4);
        var terminated = false;

        foreach (var arg in args)
        {
            if (terminated)
            {
                expanded.Add(arg);
                continue;
            }

            if (arg == EndOfOptionsToken)
            {
                terminated = true;
                expanded.Add(arg);
                continue;
            }

            expanded.AddRange(SplitOptionAssignment(arg));
        }

        return expanded.ToArray();
    }

    /// <summary>
    /// Splits tokens of the form <c>--name=value</c> or <c>--name:value</c> into two tokens so the
    /// option-parsing loop sees them as "option" + "value". Honors the <c>[key]</c> map suffix:
    /// <c>--config[env]=prod</c> splits correctly into <c>--config[env]</c> and <c>prod</c>; an
    /// <c>=</c> or <c>:</c> appearing *inside* <c>[…]</c> is left alone.
    /// </summary>
    /// <remarks>
    /// Applies to the short form too (<c>-f=bar</c> → <c>-f</c> <c>bar</c>), which
    /// <c>CliInvocation_FromArgs_Should.Split_Option_Assignment_Syntax</c> has pinned since the port.
    /// That is the modern-parser reading (clap, System.CommandLine) rather than strict <c>getopt</c>,
    /// where the value would be <c>=bar</c> — a settled decision, and not one to reopen inside a
    /// ticket about the long form. POSIX short gluing (<c>-f bar</c> ≡ <c>-fbar</c>) is unaffected and
    /// still handled by <see cref="CliShortOptionExpander"/>.
    /// </remarks>
    private static IEnumerable<string> SplitOptionAssignment(string token)
    {
        if (token.Length < 2 || token[0] != '-')
        {
            yield return token;
            yield break;
        }

        int bracket = token.IndexOf('[');
        int sep = -1;
        for (int i = 1; i < token.Length; i++)
        {
            var c = token[i];
            if (c == '=' || c == ':')
            {
                // Inside a [...] span — let the map-syntax parser handle it.
                if (bracket >= 0 && i > bracket)
                {
                    int closing = token.IndexOf(']', bracket);
                    if (closing > i) continue;
                }
                sep = i;
                break;
            }
        }

        if (sep <= 0 || sep >= token.Length - 1)
        {
            yield return token;
            yield break;
        }

        yield return token.Substring(0, sep);
        yield return token.Substring(sep + 1);
    }

    /// <summary>
    /// Renders the command line as a string. Supported format specifiers:
    /// <list type="bullet">
    ///   <item><description><c>"G"</c> (default) — readable round-trip form: executable + segments + options with their values, quoting whitespace where needed.</description></item>
    ///   <item><description><c>"D"</c> — debug form: same as <c>"G"</c> but collection options collapse to <c>name ..</c> and map options to <c>name[..] ..</c>, useful in debugger displays.</description></item>
    /// </list>
    /// Any other (or null) format value falls through to <c>"G"</c>. <paramref name="formatProvider"/>
    /// is accepted for the <see cref="IFormattable"/> contract but ignored.
    /// </summary>
    /// <example><code>
    /// var invocation = CliInvocation.FromArgs("myapp build --tags a b c");
    /// Console.WriteLine(invocation.ToString("D")); // "myapp build --tags .."
    /// </code></example>
    public string ToString(string? format, IFormatProvider? formatProvider = null)
    {
        var builder = new StringBuilder(Segments
            .Prepend(ExecutableName)
            .Select(item => item.HasWhiteSpaces() ? item.Quote() : item)
            .Join(" "));

        if (string.Equals(format, "D", StringComparison.OrdinalIgnoreCase))
        {
            Options
                .OfType<CliFlagOptionCapture>()
                .Select(o => o.Name.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ForEach(o => builder.Append($" {o}"));

            Options
                .OfType<ICliCollectionCapture>()
                .Select(o => o.Name.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ForEach(o => builder.Append($" {o} .."));

            Options
                .OfType<ICliMapOptionCapture>()
                .Select(o => o.Name.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ForEach(o => builder.Append($" {o}[..] .."));
        }
        else
        {
            string QuoteIfHasWhiteSpaces(string value) => value.HasWhiteSpaces() ? value.Quote() : value;
            Options
                .OfType<CliFlagOptionCapture>()
                .Select(o => o.Name.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ForEach(o => builder.Append($" {o}"));

            Options
                .OfType<ICliCollectionCapture>()
                .GroupBy(o => o.Name.ToLowerInvariant(), o => o.Values, StringComparer.OrdinalIgnoreCase)
                .Select(grp => new
                {
                    Name = grp.Key,
                    Values = IsRedacted(grp.Key)
                        ? Redacted
                        : grp
                            .AsEnumerable()
                            .SelectMany(values => values)
                            .Select(QuoteIfHasWhiteSpaces)
                            .Join(" ")
                })
                .ForEach(o => builder.Append($" {o.Name} {o.Values}"));

            Options
                .OfType<ICliMapOptionCapture>()
                .ForEach(o =>
                {
                    var head = $" {o.Name}[{QuoteIfHasWhiteSpaces(o.Key)}]";
                    switch (o)
                    {
                        case CliKeyValueOptionCapture kv:
                            builder.Append($"{head} {(IsRedacted(kv.Name) ? Redacted : QuoteIfHasWhiteSpaces(kv.Value))}");
                            break;
                        case CliKeyCollectionOptionCapture kc:
                            builder.Append($"{head} {(IsRedacted(kc.Name)
                                ? Redacted
                                : kc.Values.Select(QuoteIfHasWhiteSpaces).Join(" "))}");
                            break;
                        case CliKeyFlagOptionCapture:
                            builder.Append(head); // keyed flag: presence only, no value
                            break;
                    }
                });
        }


        return builder.ToString();
    }

    /// <summary>
    /// Returns a string representation of the command line using general formatting.
    /// </summary>
    /// <returns>
    /// A <see cref="string"/> representing the original command line.
    /// </returns>
    /// <example><code>
    /// var line = CliInvocation.FromArgs("myapp deploy --verbose").ToString();
    /// </code></example>
    public override string ToString()
    {
        return ToString("G", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Implicitly converts a <see cref="CliInvocation"/> instance to a <see cref="string"/> representation.
    /// </summary>
    /// <param name="invocation">The <see cref="CliInvocation"/> instance to convert.</param>
    /// <returns>
    /// A <see cref="string"/> that represents the command line as a string.
    /// </returns>
    /// <remarks>
    /// This operator allows a <see cref="CliInvocation"/> instance to be implicitly converted to a string
    /// by calling its <see cref="ToString()"/> method, which returns the original command line.
    /// </remarks>
    /// <example><code>
    /// string line = CliInvocation.FromArgs("myapp deploy --verbose");
    /// </code></example>
    public static implicit operator string(CliInvocation invocation) => invocation.ToString();

}

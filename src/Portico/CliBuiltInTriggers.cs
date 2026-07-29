using System.Text.RegularExpressions;

namespace Portico;

/// <summary>
/// The tokens the framework answers by itself when a command has not claimed them — <c>--help</c>,
/// <c>help</c>, <c>-h</c>, <c>-?</c>, <c>?</c>, <c>--version</c> and <c>-V</c>.
/// </summary>
/// <remarks>
/// One definition, because two would drift. <see cref="CliApplication"/> matches an invocation
/// against these to decide whether to render help or the version, and the scalar option materializer
/// consults the same set to explain a confusing failure: a route that declares <c>--help</c> as its
/// own option <em>wins</em> over the built-in (SOL-75, which is what makes <c>-h</c> for
/// <c>--host</c> work), so a user typing <c>--help</c> gets a type error about the word "help" and
/// nothing pointing at the cause (POR-120).
/// <para>
/// These are the <b>defaults only</b>. An application may replace them through
/// <c>WithHelp(h => h.Triggers(...))</c> / <c>WithVersion(v => v.Triggers(...))</c>, and where the effective set matters —
/// deciding what to render, and warning an author that a route has shadowed a trigger — the
/// configured list is used instead. This set answers the narrower question "is this token one a
/// reader would expect the framework to handle?", which is what an error message needs.
/// </para>
/// </remarks>
internal static partial class CliBuiltInTriggers
{
    [GeneratedRegex(@"^(?:--help|help|-h|-\?|\?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    public static partial Regex HelpSignal();

    [GeneratedRegex(@"^(?:--version|-V)$", RegexOptions.CultureInvariant)]
    public static partial Regex VersionSignal();

    /// <summary>
    /// The option-form default triggers, in the order a message should list them. The segment forms
    /// (<c>help</c>, <c>?</c>) are excluded: they are positional, so a route cannot shadow them with
    /// an option alias.
    /// </summary>
    public static readonly string[] OptionFormHelpTriggers = ["--help", "-h", "-?"];

    /// <summary>The option-form default version triggers.</summary>
    public static readonly string[] OptionFormVersionTriggers = ["--version", "-V"];

    /// <summary>
    /// True when <paramref name="token"/> is a token the framework would have answered itself, had a
    /// route not claimed it. Used to explain the failure rather than to route.
    /// </summary>
    public static bool IsDefaultTrigger(string token) =>
        HelpSignal().IsMatch(token) || VersionSignal().IsMatch(token);
}

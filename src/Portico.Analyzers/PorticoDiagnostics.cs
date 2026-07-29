using Microsoft.CodeAnalysis;

namespace Portico.Analyzers;

/// <summary>
/// Central registry of descriptors for every Portico build-time diagnostic. IDs use the
/// <c>POR</c> prefix and three digits. Every descriptor carries a <c>helpLinkUri</c> into the
/// canonical analyzer-rules reference in the docs.
/// </summary>
internal static class PorticoDiagnostics
{
    // Canonical per-rule reference; anchors are the lower-cased rule id (e.g. HelpBase + "por001").
    // Defined once so the links cannot drift. The target page is authored by POR-7.
    private const string HelpBase =
        "https://github.com/vvka-141/portico/blob/main/docs/reference/analyzer-rules.md#";

    private const string Category = "Portico";

    /// <summary>
    /// POR001: a <c>{placeholder}</c> in a <c>[CliRoute]</c> string has no matching parameter
    /// on the decorated method. The same check runs at <c>CliApplication.Create</c> time as a
    /// runtime safety net for builds without the analyzer.
    /// </summary>
    public static readonly DiagnosticDescriptor RoutePlaceholderMismatch = new(
        id: "POR001",
        title: "Route placeholder does not match any parameter",
        messageFormat:
            "Route placeholder '{{{0}}}' on '{1}' binds to a parameter of the same name, but '{1}' " +
            "has none called '{0}'. Available parameters: {2}. Rename the placeholder to one of those, " +
            "or add a parameter '{0}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A {paramName} token inside a [CliRoute] string must match a parameter on the " +
            "decorated method. Detected at compile time by the Portico analyzer; the runtime " +
            "check at CliApplication.Create is a safety net for builds without the analyzer.",
        helpLinkUri: HelpBase + "por001");

    /// <summary>
    /// POR002: two methods declare an identical <c>[CliRoute]</c> signature. The same check runs
    /// at <c>CliApplication.Create</c> time (<c>CliConfigurationException.DuplicateRoute</c>);
    /// the analyzer catches it earlier, at IDE-typing time.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateRoute = new(
        id: "POR002",
        title: "Duplicate [CliRoute] signature on one type",
        messageFormat:
            "Route '{0}' is declared twice on the same type, by both '{1}' and '{2}'. One of them " +
            "can never be reached — rename one, or give it a distinct subcommand prefix.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Two [CliRoute] methods on the same type declare the same route signature, so one of " +
            "them is unreachable. Scoped to the declaring type on purpose: two different contracts " +
            "may legally declare the same route and be mounted under different root routes (or only " +
            "one of them registered), which the analyzer cannot see. The framework still rejects a " +
            "genuine collision at CliApplication.Create, where the registrations are visible.",
        helpLinkUri: HelpBase + "por002",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// POR003: a <c>[CliOption]</c> spec is empty, whitespace, or doesn't describe valid
    /// aliases. Valid: <c>"--verbose"</c>, <c>"--verbose|-v"</c>. Invalid: <c>""</c>, <c>" "</c>,
    /// <c>"verbose"</c> (missing dashes), <c>"--"</c> (just dashes), <c>"--verbose|"</c>
    /// (trailing pipe).
    /// </summary>
    public static readonly DiagnosticDescriptor MalformedCliOptionSpec = new(
        id: "POR003",
        title: "Malformed [CliOption] spec",
        messageFormat:
            "[CliOption] spec '{0}' is invalid: {1}. Valid form is a pipe-separated list of " +
            "dash-prefixed aliases (e.g. \"--verbose|-v\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every [CliOption] spec must be a pipe-separated list of one or more aliases, each " +
            "starting with '-' (short form, 1 char after the dash) or '--' (long form, 1+ chars " +
            "after the dashes). Empty segments, whitespace-only entries, and undashed names are " +
            "rejected. Early detection prevents silent runtime 'option not recognized' errors.",
        helpLinkUri: HelpBase + "por003");

    /// <summary>
    /// POR004: a method decorated with <c>[CliRoute]</c> has no <c>[CliCommandExample]</c>.
    /// "Examples are tests" is the signature feature — a route without one has nothing to test
    /// against <c>CliContractValidator&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <b>Error, not Warning (POR-76).</b> Examples-are-tests is stated as a non-negotiable
    /// invariant, and an invariant enforced at Warning is a suggestion — it holds only in projects
    /// that happen to set <c>TreatWarningsAsErrors</c>. The README claimed this rule "fails the
    /// build"; at Warning that was false for an ordinary consumer, which is an overclaim about the
    /// one mechanism the framework is chosen for.
    /// <para>
    /// The usual objection to Error severity — that it interrupts the natural authoring order of
    /// writing the route first and the example second — is answered by
    /// <c>MissingCommandExampleCodeFix</c>, which inserts a stub that compiles. The interruption
    /// costs one keystroke.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MissingCommandExample = new(
        id: "POR004",
        title: "Missing [CliCommandExample] on [CliRoute] method",
        messageFormat:
            "Method '{0}' is decorated with [CliRoute] but has no [CliCommandExample]. Add one — " +
            "e.g. [CliCommandExample(\"<the command as a user would type it>\")] — it both documents " +
            "the command and becomes an executable CliContractValidator<T> test.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every [CliRoute] method must declare at least one [CliCommandExample]. Examples " +
            "serve as both help documentation and executable test cases (via CliContractValidator<T>) " +
            "— the signature feature of the framework. A route with no example has nothing to " +
            "verify, so the contract it advertises is unchecked.",
        helpLinkUri: HelpBase + "por004");

    /// <summary>
    /// POR005: a <c>[CliArgument]</c> decorates a parameter the method's <c>[CliRoute]</c> declares
    /// no <c>{placeholder}</c> for. The route string is the command's path in full, so an argument
    /// the route does not mention has no position to bind to. The mirror image of POR001, which
    /// reports a placeholder with no parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor CliArgumentParameterMismatch = new(
        id: "POR005",
        title: "[CliArgument] has no matching route placeholder",
        messageFormat:
            "[CliArgument] on parameter '{1}' of '{0}' has no matching placeholder in the route \"{2}\". " +
            "Put the argument in the route: [CliRoute(\"{3}\")].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A command's path shape is declared entirely by [CliRoute], exactly as an ASP.NET Core " +
            "route template declares {id} inline. [CliArgument] describes an argument the route " +
            "already declares — it supplies the help text and display name and never adds a segment. " +
            "An argument with no placeholder would have no position to bind to.",
        helpLinkUri: HelpBase + "por005");

    /// <summary>
    /// POR006: a class that transitively extends <c>CliOptions</c> or <c>CliMiddleware</c> must
    /// have a public parameterless constructor. The same check runs at
    /// <c>CliApplication.Create</c> time for <c>CliOptions</c> bundles.
    /// </summary>
    public static readonly DiagnosticDescriptor BundleMissingParameterlessCtor = new(
        id: "POR006",
        title: "CliOptions bundle must have a public parameterless constructor",
        messageFormat:
            "'{0}' extends {1} but lacks a public parameterless constructor. " +
            "Option bundles are instantiated per-invocation via Activator.CreateInstance — " +
            "move dependencies out of the constructor or expose them as [CliOption] properties.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A CliOptions bundle is instantiated once per command invocation via " +
            "Activator.CreateInstance(), which requires a public parameterless constructor. The " +
            "framework throws CliConfigurationException at Create time; the analyzer catches it in " +
            "the IDE. CliMiddleware is NOT covered by this rule: middleware is supplied as an " +
            "instance to UseMiddleware(...) and cloned per dispatch, so a constructor dependency " +
            "is legitimate and is how a DI container injects into it.",
        helpLinkUri: HelpBase + "por006");

    // POR007 was retired in POR-79. It reported a parameter carrying more than one [CliArgument] —
    // a mistake that existed only because CliArgumentAttribute declared AllowMultiple = true and
    // then banned what it had just permitted. Setting AllowMultiple = false hands the check to the
    // C# compiler (CS0579), which is strictly stronger: no analyzer reference, no suppression, no
    // way to turn it off. The ID is not reused; the next free rule is POR012.

    /// <summary>
    /// POR008: a method decorated with <c>[CliRoute]</c> must return <c>int</c> or
    /// <c>Task&lt;int&gt;</c>. Other return types (<c>void</c>, <c>async void</c>, <c>Task</c>,
    /// custom types) fail to dispatch at runtime.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidCliRouteReturnType = new(
        id: "POR008",
        title: "[CliRoute] method has an invalid return type",
        messageFormat:
            "Method '{0}' is decorated with [CliRoute] and returns '{1}'. Only 'int' and " +
            "'Task<int>' are supported — return your exit code (0 = success, 1 = runtime error, " +
            "2 = usage error, 130 = cancelled) or throw CliExitException for error paths.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Command handlers must return an exit code: 'int' for synchronous methods or " +
            "'Task<int>' for async. 'void' / 'async void' / non-generic 'Task' are forbidden — " +
            "they can't carry an exit code and 'async void' is particularly hostile because " +
            "exceptions in it crash the process.",
        helpLinkUri: HelpBase + "por008");

    /// <summary>
    /// POR009: two <c>[CliOption]</c> declarations on one command — parameters, bundle properties,
    /// or one of each — claim the same alias. At dispatch both would bind the same captured value,
    /// which is almost never what the author meant. Rejected at
    /// <c>CliApplication.Create</c>; the analyzer moves it into the edit loop.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateOptionAlias = new(
        id: "POR009",
        title: "Two options on one command declare the same alias",
        messageFormat:
            "Option alias '{0}' is declared by both {1} and {2}. Each alias must be unique per " +
            "command — two options binding the same alias would silently receive the same value at " +
            "dispatch. Rename one, or give it a distinct alias.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "An alias identifies exactly one option. When two [CliOption] declarations on the same " +
            "command claim it, both bind the same captured value — silently shared state that " +
            "almost never matches intent. Note that single-character short aliases are compared " +
            "case-SENSITIVELY (so '-v' and '-V' are distinct, preserving the curl idiom), while " +
            "longer aliases are compared case-insensitively (so '--name' and '--NAME' collide).",
        helpLinkUri: HelpBase + "por009");

    /// <summary>
    /// POR009 variant: one <c>[CliOption]</c> declaration's spec lists aliases that collide with
    /// each other under <c>CliAliasComparer</c> — typically <c>--name|--NAME</c>. Uses the same
    /// diagnostic ID as <see cref="DuplicateOptionAlias"/> (both are duplicate-alias errors) with a
    /// message that names the single declaration rather than two.
    /// </summary>
    public static readonly DiagnosticDescriptor SelfDuplicateOptionAlias = new(
        id: "POR009",
        title: "Option spec declares the same alias twice",
        messageFormat:
            "[CliOption] on {0} declares alias '{1}' twice. '{2}' and '{1}' differ only by case, " +
            "and long aliases are compared case-insensitively.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A single [CliOption] specification lists two aliases that are the same under the " +
            "framework's alias comparison rule: single-character short aliases are case-SENSITIVE " +
            "('-v' and '-V' are distinct), while longer aliases are case-insensitive ('--name' and " +
            "'--NAME' collide). Remove one of the two aliases from the pipe-separated specification.",
        helpLinkUri: HelpBase + "por009");

    /// <summary>
    /// POR010: a <c>[CliOption]</c> parameter or property whose type cannot be built from a
    /// command-line string — no <c>TypeConverter</c> can produce it. The runtime rejects it at
    /// <c>CliApplication.Create</c>; the analyzer catches it while the type is still on screen.
    /// </summary>
    /// <remarks>
    /// Deliberately conservative: it fires only for a type declared in the user's own compilation,
    /// because whether an arbitrary <em>referenced</em> type has a converter is a runtime fact
    /// (<c>TypeDescriptor</c>'s intrinsic table is not visible to Roslyn). A false positive here
    /// would fail a build that works — worse than the runtime backstop it replaces.
    /// </remarks>
    public static readonly DiagnosticDescriptor UnconvertibleOptionType = new(
        id: "POR010",
        title: "[CliOption] type cannot be built from a command-line string",
        messageFormat:
            "Option '{0}' has type '{1}', which cannot be converted from a command-line string. " +
            "Everything a user types is a string: give '{1}' a [TypeConverter] that converts from " +
            "string, or use a type that already has one (a primitive, enum, string, TimeSpan, Guid, " +
            "Uri, DateTime, or a collection of those).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A command-line value arrives as text. Portico binds it through TypeDescriptor, so an " +
            "option's type must have a TypeConverter that can convert from string. A type of your " +
            "own with no converter cannot be bound, and the framework rejects it at " +
            "CliApplication.Create — this rule moves that failure to the build.",
        helpLinkUri: HelpBase + "por010");

    /// <summary>
    /// POR011: a <c>[CliRoute]</c> string repeats the same <c>{placeholder}</c> name. The second
    /// slot overwrites the first at dispatch — a silent data loss that <c>CliContractValidator</c>
    /// does not catch. The runtime rejects it at <c>CliApplication.Create</c>; the analyzer moves
    /// it into the edit loop.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateRoutePlaceholder = new(
        id: "POR011",
        title: "Route declares the same placeholder twice",
        messageFormat:
            "Route \"{0}\" on '{1}' repeats placeholder '{{{2}}}'. Each placeholder name must " +
            "appear once — use distinct names for distinct positions (e.g. '{{src}}' and '{{dst}}').",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A repeated {placeholder} in a [CliRoute] string resolves both slots to the same " +
            "parameter. At dispatch the second value overwrites the first — silent data loss that " +
            "the contract validator does not catch, making it a false green in the framework's " +
            "central verification mechanism. Use distinct placeholder names for distinct positions.",
        helpLinkUri: HelpBase + "por011");

    // POR012 is reserved for POR-80 ("bool used where CliFlag? was meant"), allocated during backlog
    // refinement on 2026-07-29 after that ticket lost the ID twice to rules that shipped ahead of it.
    // This manifest — not a ticket summary — is the register: read it before claiming an ID.

    /// <summary>
    /// POR013: a <c>catch</c> clause in a command handler swallows <c>CliExitException</c>, so a
    /// failed command returns the handler's exit code instead of the one it asked for.
    /// </summary>
    /// <remarks>
    /// <b>Warning, not Error.</b> A catch-all is legal C# with defensible uses, and it does not break
    /// a framework guarantee the way a route with no example does (POR004). Revisit if it proves a
    /// common trap.
    /// <para>
    /// <b>This cannot be enforced at run time and should not be attempted</b> — see
    /// <c>docs/ROADMAP.md</c>, "Resolved decisions". No CLR mechanism makes a managed exception
    /// uncatchable, and every ambient workaround (<c>FirstChanceException</c>, an <c>AsyncLocal</c>
    /// "exit requested" flag) is unable to tell "swallowed by accident" from "caught on purpose".
    /// Overriding a handler's returned exit code on that guess is worse than the bug.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor SwallowedCliExitException = new(
        id: "POR013",
        title: "Catch clause swallows CliExitException",
        messageFormat:
            "This '{0}' in command handler '{1}' swallows CliExitException, so a controlled " +
            "exit is downgraded to whatever the handler returns — a failed command can exit 0. Add " +
            "'when (ex is not CliExitException)', or rethrow it.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "CliExitException is the controlled-exit mechanism: the framework catches it at the " +
            "application boundary, writes its message to stderr and returns its ExitCode. A catch-all " +
            "between the throw and that boundary defeats it silently, and the failure is invisible in " +
            "review — 'catch (Exception ex) { _log.Error(ex); return 1; }' is ordinary defensive C#. " +
            "For a CI step, a Kubernetes job or a deployment gate, which read the exit code and " +
            "nothing else, that is a green build over a failed migration. The analyzer sees the " +
            "handler body only: an exception swallowed by a catch-all several frames deep is out of " +
            "reach, so this closes the common case rather than the general one.",
        helpLinkUri: HelpBase + "por013");
}

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
            "Route placeholder '{{{0}}}' on '{1}' does not match any parameter. " +
            "Available parameters: {2}. " +
            "Rename the placeholder or add a parameter with this name.",
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
        title: "Duplicate [CliRoute] signature",
        messageFormat:
            "Route '{0}' is declared by both '{1}' and '{2}'. Each route must be unique — " +
            "rename one, or disambiguate by adding a subcommand prefix to one of the routes.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Two or more [CliRoute] methods declare the same route signature. The framework " +
            "throws CliConfigurationException.DuplicateRoute at Create time; the analyzer " +
            "catches it earlier.",
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
    public static readonly DiagnosticDescriptor MissingCommandExample = new(
        id: "POR004",
        title: "Missing [CliCommandExample] on [CliRoute] method",
        messageFormat:
            "Method '{0}' is decorated with [CliRoute] but has no [CliCommandExample]. " +
            "Add at least one example — it both documents the command and drives CliContractValidator<T> tests.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Every [CliRoute] method should declare at least one [CliCommandExample]. Examples " +
            "serve as both help documentation and executable test cases (via CliContractValidator<T>) " +
            "— the signature feature of the framework.",
        helpLinkUri: HelpBase + "por004");

    /// <summary>
    /// POR005: a method-level <c>[CliArgument(parameterName, description)]</c> references a
    /// <c>parameterName</c> that does not match any parameter on the decorated method — usually
    /// a stale <c>nameof(...)</c> left behind after a rename. The argument would bind to nothing
    /// at runtime. The <c>[CliArgument]</c> analogue of POR001.
    /// </summary>
    public static readonly DiagnosticDescriptor CliArgumentParameterMismatch = new(
        id: "POR005",
        title: "[CliArgument] references an unknown parameter",
        messageFormat:
            "[CliArgument] on '{0}' references parameter '{1}', which does not exist. " +
            "Available parameters: {2}. Fix the parameter name (or the nameof) so the argument binds.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The method-level [CliArgument(parameterName, description)] form binds a positional " +
            "argument to a parameter by name. A stale nameof(...) after a rename, or a typo, leaves " +
            "the argument unbound at runtime. Detected at compile time, mirroring POR001 for route " +
            "placeholders.",
        helpLinkUri: HelpBase + "por005");

    /// <summary>
    /// POR006: a class that transitively extends <c>CliOptions</c> or <c>CliMiddleware</c> must
    /// have a public parameterless constructor. The same check runs at
    /// <c>CliApplication.Create</c> time for <c>CliOptions</c> bundles.
    /// </summary>
    public static readonly DiagnosticDescriptor BundleMissingParameterlessCtor = new(
        id: "POR006",
        title: "CliOptions/CliMiddleware subclass must have a public parameterless constructor",
        messageFormat:
            "'{0}' extends {1} but lacks a public parameterless constructor. " +
            "Bundles and middleware are instantiated per-invocation via Activator.CreateInstance — " +
            "move dependencies out of the constructor or expose them as [CliOption] properties.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "CliOptions bundles and CliMiddleware subclasses are instantiated once per command " +
            "invocation via Activator.CreateInstance(), which requires a public parameterless " +
            "constructor. The framework throws CliConfigurationException at Create time; the " +
            "analyzer catches it in the IDE.",
        helpLinkUri: HelpBase + "por006");

    /// <summary>
    /// POR007: a single parameter is targeted by more than one <c>[CliArgument]</c>.
    /// <c>[AttributeUsage(AllowMultiple = true)]</c> lets the compiler accept this, but the
    /// framework binds exactly one, so the extras silently misbind.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateCliArgument = new(
        id: "POR007",
        title: "Parameter is targeted by more than one [CliArgument]",
        messageFormat:
            "Parameter '{0}' on '{1}' is targeted by {2} [CliArgument] attributes. " +
            "Declare each argument exactly once — method-level nameof OR parameter-level, not both.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "CliArgumentAttribute is AllowMultiple=true so the method-level and parameter-level " +
            "forms can coexist on a method — but a given parameter must be declared in exactly one " +
            "place. Two or more [CliArgument] targeting the same parameter is a silent misbinding " +
            "the framework rejects at configuration time; the analyzer catches it at compile time.",
        helpLinkUri: HelpBase + "por007");

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
}

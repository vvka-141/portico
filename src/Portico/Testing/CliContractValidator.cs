
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Portico.Testing;

/// <summary>
/// Contract validator for CLI interfaces. Every <c>[CliCommandExample]</c> on
/// <typeparamref name="T"/> is executed against a <see cref="DispatchProxy"/>-backed
/// application; examples that don't match a route fail. Usage:
/// <code>
/// [Fact]
/// public void Every_Example_Resolves() =>
///     new CliContractValidator&lt;IMyCommands&gt;()
///         .Validate(onNotInvoked: ex =&gt; Assert.Fail($"Example didn't match: {ex.Example}"));
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Validate"/> answers "did it dispatch?". <see cref="Enumerate"/> additionally reports
/// <em>which</em> handler each example reached and <em>what values</em> the framework bound to it —
/// which is what makes an example a contract rather than a smoke test. An example that begins
/// dispatching to a different overload, or binding a different value, still dispatches; only
/// <see cref="CliContractExample.Handler"/> and <see cref="CliContractExample.Arguments"/> catch it.
/// </para>
/// <para>
/// A contract composed into a master CLI under a root route
/// (<c>AddCommands(tool, [new CliRouteAttribute("aws")])</c>) must be validated <em>in the position
/// it ships in</em> — pass those root routes to the constructor. Validating the unmounted surface of
/// a contract that only ever ships mounted proves nothing about the CLI users actually get: the
/// examples would pass here and exit 2 there (POR-40).
/// </para>
/// </remarks>
public sealed class CliContractValidator<T> where T : class
{
    private readonly string[] _rootRoutes;

    /// <summary>
    /// Validates <typeparamref name="T"/> in the position it ships in.
    /// </summary>
    /// <param name="rootRoutes">
    /// The root routes the contract is mounted under in the real application — the same values
    /// passed to <c>AddCommands(instance, rootRoutes)</c>. Omit them for a contract registered at
    /// the root. Each example is run against the mounted route, so an example that does not
    /// dispatch in the composed CLI fails here.
    /// </param>
    /// <example><code>
    /// // Program.cs:  cfg.AddCommands(new AwsTool(), [new CliRouteAttribute("aws")])
    /// new CliContractValidator&lt;IAwsTool&gt;("aws")
    ///     .Validate(onNotInvoked: ex =&gt; Assert.Fail($"Example didn't dispatch: {ex.Example}"));
    /// </code></example>
    public CliContractValidator(params string[] rootRoutes) =>
        _rootRoutes = rootRoutes ?? [];

    /// <summary>
    /// Runs every <c>[CliCommandExample]</c> on <typeparamref name="T"/> through a
    /// <see cref="DispatchProxy"/>-backed application and reports which examples matched
    /// and which didn't.
    /// </summary>
    /// <param name="onNotInvoked">
    /// Invoked for each example that failed to reach the proxy, with <strong>the reason it
    /// failed</strong> — the framework's own diagnostic ("Unrecognized option(s): --bogus",
    /// "Value 'abc' for option '--amount' is invalid."). Put it in the assertion message: a red test
    /// that says only <em>that</em> an example broke sends the reader hunting for the <em>why</em>
    /// the framework already knew.
    /// </param>
    /// <param name="onInvoked">Invoked for each example that successfully reached the proxy.</param>
    /// <param name="configureApplication">
    /// Optional additional configuration applied after the contract's DispatchProxy service is
    /// registered. Use this to add middleware, env-var fallbacks, or other configuration that
    /// your examples rely on — e.g., if your contract's examples use <c>--verbose</c> or
    /// <c>--output</c> options defined on a <see cref="CliMiddleware"/>, register that middleware
    /// here so the validator recognizes them.
    /// </param>
    /// <example><code>
    /// new CliContractValidator&lt;IMyCommands&gt;()
    ///     .Validate(onNotInvoked: (ex, reason) =&gt;
    ///         Assert.Fail($"Example didn't dispatch: {ex.Example}{Environment.NewLine}Reason: {reason}"));
    /// </code></example>
    public void Validate(
        Action<CliCommandExampleAttribute, string> onNotInvoked,
        Action<CliCommandExampleAttribute>? onInvoked = default,
        Action<ICliApplicationBuilder>? configureApplication = default)
    {
        ThrowIf.ArgumentNull(onNotInvoked);
        onInvoked ??= (tc) => Debug.WriteLine($"Passed: {tc.Example}");

        foreach (var result in Run(configureApplication))
        {
            if (result.Dispatch is not null)
            {
                onInvoked(result.Attribute);
            }
            else
            {
                onNotInvoked(result.Attribute, result.FailureReason ?? UnknownReason);
            }
        }
    }

    private const string UnknownReason =
        "the example did not reach a route, and the framework emitted no diagnostic.";

    /// <summary>
    /// Runs every <c>[CliCommandExample]</c> on <typeparamref name="T"/> and returns one plain
    /// <see cref="CliContractExample"/> descriptor per example, each carrying whether it matched a
    /// route, <strong>which handler it reached</strong>, and <strong>what values were bound</strong>.
    /// Feed these into a data-driven test (xUnit <c>[Theory]</c>/<c>MemberData</c>, NUnit
    /// <c>TestCaseSource</c>) to get <strong>one test case per example</strong> — a "3 of 20 failed"
    /// signal rather than a single red test. The return type carries no test-framework dependency;
    /// the <c>[Theory]</c> wiring stays in your test project.
    /// </summary>
    /// <param name="configureApplication">
    /// Optional additional configuration applied after the contract's DispatchProxy service is
    /// registered (e.g. register a <see cref="CliMiddleware"/> whose options your examples use).
    /// </param>
    /// <example><code>
    /// var e = new CliContractValidator&lt;IMyCommands&gt;().Enumerate()
    ///     .Single(x =&gt; x.Example == "db seed --rows 100");
    ///
    /// Assert.True(e.Matched);
    /// Assert.Equal(nameof(IMyCommands.Seed), e.Handler);   // the route, pinned
    /// Assert.Equal(100, e.Arguments["rows"]);              // the binding, pinned
    /// </code></example>
    public IReadOnlyList<CliContractExample> Enumerate(
        Action<ICliApplicationBuilder>? configureApplication = default) =>
        Run(configureApplication)
            .Select(r => new CliContractExample(
                r.Attribute.Example,
                r.Attribute.Description,
                r.Dispatch is not null,
                r.Dispatch?.Handler,
                r.Dispatch?.Arguments ?? EmptyArguments,
                r.Dispatch is null ? r.FailureReason ?? UnknownReason : null))
            .ToArray();

    private static readonly IReadOnlyDictionary<string, object?> EmptyArguments =
        new Dictionary<string, object?>(0);

    /// <summary>
    /// Shared engine for <see cref="Validate"/> and <see cref="Enumerate"/>: validates that
    /// <typeparamref name="T"/> is an interface carrying at least one example, spins up a single
    /// <see cref="DispatchProxy"/>-backed application, and runs each example — pairing every
    /// attribute with the dispatch it produced, or <see langword="null"/> if it reached no route.
    /// </summary>
    private IReadOnlyList<(CliCommandExampleAttribute Attribute, CliDispatch? Dispatch, string? FailureReason)> Run(
        Action<ICliApplicationBuilder>? configureApplication)
    {
        var type = typeof(T);
        if (false == type.IsInterface)
        {
            throw new InvalidOperationException(
                $"CliContractValidator<T> requires T to be an interface. '{type.FullName}' is not an interface. " +
                "Declare your CLI contract as an interface, decorate its methods with [CliRoute]/[CliArgument]/[CliOption] " +
                "and [CliCommandExample], then implement the interface on your service class.");
        }

        var testCases = type
            .GetInterfaces()
            .Union([type])
            .SelectMany(t => t.GetMethods())
            .Distinct()
            .SelectMany(mi => mi.GetCustomAttributes(typeof(CliCommandExampleAttribute), true))
            .Cast<CliCommandExampleAttribute>()
            .ToArray();

        if (testCases.Length == 0)
        {
            throw new InvalidOperationException(
                $"CliContractValidator<{type.Name}> found no [CliCommandExample] attributes on the contract. " +
                "Examples are the test cases: decorate at least one method on the interface with " +
                "[CliCommandExample(\"...\")] to define the commands the contract promises to accept.");
        }

        T proxy = DispatchProxy.Create<T, CliRouteProxy>();
        var recorder = (CliRouteProxy)(object)proxy;

        // The framework already knows why an example failed — it writes the diagnostic to its
        // console. Capturing that console is what turns "example didn't dispatch" into an actionable
        // failure (POR-29). It also stops the validator from spraying those diagnostics into the
        // user's test log as a side effect of asking a question.
        //
        // WithConsole runs BEFORE configureApplication, so a caller who supplies their own console
        // still wins — they just get no reason, which is their choice to make.
        var console = new CapturingConsole();

        var app = CliApplication
            .Create(builder =>
            {
                builder.WithConsole(console);
                builder.AddCommands(proxy, _rootRoutes.Select(r => new CliRouteAttribute(r)));
                configureApplication?.Invoke(builder);
            });

        // The example is authored against the contract, which cannot know its mount — so the mount
        // is what we prepend before running it, exactly as help does (POR-39).
        var mount = _rootRoutes.Length == 0
            ? string.Empty
            : string.Join(' ', _rootRoutes) + " ";

        var results = new List<(CliCommandExampleAttribute, CliDispatch?, string?)>(testCases.Length);
        foreach (var testCase in testCases)
        {
            Debug.WriteLine(testCase.Example);
            Debug.WriteLine(testCase.Description);

            // The proxy records the dispatch it receives. A non-zero exit means the framework
            // rejected the example before reaching any handler; a zero exit with no recorded
            // dispatch would mean the framework short-circuited (--help, --version), which is
            // not a route match and must not be reported as one.
            recorder.Clear();
            console.Clear();
            int exitCode = app.Run($"program {mount}{testCase.Example}");
            var dispatch = exitCode == 0 ? recorder.Dispatch : null;

            results.Add((testCase, dispatch, dispatch is null ? ExplainFailure(console, exitCode) : null));
        }
        return results;
    }

    /// <summary>
    /// Why an example failed to dispatch, in the framework's own words. stderr is where the
    /// rejection is written; a zero exit with nothing dispatched means a help/version trigger
    /// swallowed the example, which is a distinct mistake and worth naming as such.
    /// </summary>
    private static string ExplainFailure(CapturingConsole console, int exitCode)
    {
        var error = console.ErrorWriter.ToString().Trim();
        if (error.Length > 0)
        {
            return error;
        }

        if (exitCode == CliExitException.SuccessExitCode)
        {
            return "the example exited 0 without reaching a handler — it was consumed by a built-in " +
                   "trigger (--help, --version) rather than matching a route. An example must be a " +
                   "command the contract answers.";
        }

        return $"the example exited {exitCode} without reaching a handler, and wrote no diagnostic.";
    }

    /// <summary>
    /// The in-memory console the validated application writes to. Not a public type: a user
    /// validating a contract is asking a question, and the answer belongs in the return value, not
    /// in their test log.
    /// </summary>
    private sealed class CapturingConsole : ICliConsole
    {
        public StringWriter OutWriter { get; private set; } = new();
        public StringWriter ErrorWriter { get; private set; } = new();

        public TextWriter Out => OutWriter;
        public TextWriter Error => ErrorWriter;
        public TextReader In => TextReader.Null;

        public void Clear()
        {
            OutWriter.Dispose();
            ErrorWriter.Dispose();
            OutWriter = new StringWriter();
            ErrorWriter = new StringWriter();
        }
    }

    /// <summary>
    /// The handler an example reached, and the values the framework bound to it. Recorded by
    /// <see cref="CliRouteProxy"/> at the moment of dispatch — the one place where "did this
    /// example do what it says" is actually observable.
    /// </summary>
    private sealed record CliDispatch(string Handler, IReadOnlyDictionary<string, object?> Arguments);

    // NOT sealed: DispatchProxy.Create generates a subclass of TProxy at runtime and throws
    // ArgumentException ("The base type ... cannot be sealed") if it cannot.
    private class CliRouteProxy : DispatchProxy
    {
        public CliDispatch? Dispatch { get; private set; }

        public void Clear() => Dispatch = null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null)
                throw new ArgumentNullException(nameof(targetMethod));

            // Capture BEFORE short-circuiting: the bound arguments are the whole point. Discarding
            // them is what reduced this validator to a routability smoke test — an example could
            // start dispatching to a different overload, or binding a different value, and still
            // report a pass.
            var parameters = targetMethod.GetParameters();
            var arguments = new Dictionary<string, object?>(parameters.Length, StringComparer.Ordinal);
            for (int i = 0; i < parameters.Length; i++)
            {
                var name = parameters[i].Name;
                if (name is null) continue;
                arguments[name] = args is not null && i < args.Length ? args[i] : null;
            }

            Dispatch = new CliDispatch(targetMethod.Name, arguments);

            // Empty message is non-printable by the framework's stderr discipline, so the
            // contract-validation path prints nothing. An "Ok" message here used to leak into
            // any parallel test that had redirected Console.Error via CliTestHarness.
            throw new CliExitException(string.Empty) { ExitCode = 0 };
        }

        public override string ToString() => typeof(CliContractValidator<T>).ToString();
    }
}


using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <param name="onNotInvoked">Invoked for each example that failed to reach the proxy.</param>
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
    ///     .Validate(onNotInvoked: ex =&gt; Assert.Fail($"Example didn't dispatch: {ex.Example}"));
    /// </code></example>
    public void Validate(
        Action<CliCommandExampleAttribute> onNotInvoked,
        Action<CliCommandExampleAttribute>? onInvoked = default,
        Action<ICliApplicationBuilder>? configureApplication = default)
    {
        onInvoked ??= (tc) => Debug.WriteLine($"Passed: {tc.Example}");

        foreach (var result in Run(configureApplication))
        {
            if (result.Dispatch is not null)
            {
                onInvoked(result.Attribute);
            }
            else
            {
                onNotInvoked(result.Attribute);
            }
        }
    }

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
                r.Dispatch?.Arguments ?? EmptyArguments))
            .ToArray();

    private static readonly IReadOnlyDictionary<string, object?> EmptyArguments =
        new Dictionary<string, object?>(0);

    /// <summary>
    /// Shared engine for <see cref="Validate"/> and <see cref="Enumerate"/>: validates that
    /// <typeparamref name="T"/> is an interface carrying at least one example, spins up a single
    /// <see cref="DispatchProxy"/>-backed application, and runs each example — pairing every
    /// attribute with the dispatch it produced, or <see langword="null"/> if it reached no route.
    /// </summary>
    private IReadOnlyList<(CliCommandExampleAttribute Attribute, CliDispatch? Dispatch)> Run(
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

        var app = CliApplication
            .Create(builder =>
            {
                builder.AddCommands(proxy, _rootRoutes.Select(r => new CliRouteAttribute(r)));
                configureApplication?.Invoke(builder);
            });

        // The example is authored against the contract, which cannot know its mount — so the mount
        // is what we prepend before running it, exactly as help does (POR-39).
        var mount = _rootRoutes.Length == 0
            ? string.Empty
            : string.Join(' ', _rootRoutes) + " ";

        var results = new List<(CliCommandExampleAttribute, CliDispatch?)>(testCases.Length);
        foreach (var testCase in testCases)
        {
            Debug.WriteLine(testCase.Example);
            Debug.WriteLine(testCase.Description);

            // The proxy records the dispatch it receives. A non-zero exit means the framework
            // rejected the example before reaching any handler; a zero exit with no recorded
            // dispatch would mean the framework short-circuited (--help, --version), which is
            // not a route match and must not be reported as one.
            recorder.Clear();
            int exitCode = app.Run($"program {mount}{testCase.Example}");
            var dispatch = exitCode == 0 ? recorder.Dispatch : null;

            results.Add((testCase, dispatch));
        }
        return results;
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

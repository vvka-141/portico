
using Portico.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

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
public sealed class CliContractValidator<T> where T : class
{
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

        foreach (var (attribute, matched) in Run(configureApplication))
        {
            if (matched)
            {
                onInvoked(attribute);
            }
            else
            {
                onNotInvoked(attribute);
            }
        }
    }

    /// <summary>
    /// Runs every <c>[CliCommandExample]</c> on <typeparamref name="T"/> and returns one plain
    /// <see cref="CliContractExample"/> descriptor per example, each carrying whether it matched a
    /// route. Feed these into a data-driven test (xUnit <c>[Theory]</c>/<c>MemberData</c>, NUnit
    /// <c>TestCaseSource</c>) to get <strong>one test case per example</strong> — a "3 of 20 failed"
    /// signal rather than a single red test. The return type carries no test-framework dependency;
    /// the <c>[Theory]</c> wiring stays in your test project.
    /// </summary>
    /// <param name="configureApplication">
    /// Optional additional configuration applied after the contract's DispatchProxy service is
    /// registered (e.g. register a <see cref="CliMiddleware"/> whose options your examples use).
    /// </param>
    /// <example><code>
    /// public static IEnumerable&lt;object[]&gt; Examples() =&gt;
    ///     new CliContractValidator&lt;IMyCommands&gt;().Enumerate()
    ///         .Select(e =&gt; new object[] { e.Example, e.Matched });
    ///
    /// [Theory]
    /// [MemberData(nameof(Examples))]
    /// public void Example_dispatches(string example, bool matched) =&gt;
    ///     Assert.True(matched, $"Example did not dispatch: {example}");
    /// </code></example>
    public IReadOnlyList<CliContractExample> Enumerate(
        Action<ICliApplicationBuilder>? configureApplication = default) =>
        Run(configureApplication)
            .Select(r => new CliContractExample(r.Attribute.Example, r.Attribute.Description, r.Matched))
            .ToArray();

    /// <summary>
    /// Shared engine for <see cref="Validate"/> and <see cref="Enumerate"/>: validates that
    /// <typeparamref name="T"/> is an interface carrying at least one example, spins up a single
    /// <see cref="DispatchProxy"/>-backed application, and runs each example — pairing every
    /// attribute with whether it dispatched (exit code 0).
    /// </summary>
    private IReadOnlyList<(CliCommandExampleAttribute Attribute, bool Matched)> Run(
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

        var app = CliApplication
            .Create(builder =>
            {
                builder.AddCommands(proxy);
                configureApplication?.Invoke(builder);
            });

        var results = new List<(CliCommandExampleAttribute, bool)>(testCases.Length);
        foreach (var testCase in testCases)
        {
            Debug.WriteLine(testCase.Example);
            Debug.WriteLine(testCase.Description);

            int result = app.Run($"program {testCase.Example}");
            results.Add((testCase, result == 0));
        }
        return results;
    }




    private class CliRouteProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null)
                throw new ArgumentNullException(nameof(targetMethod));

            // Empty message is non-printable by the framework's stderr discipline, so the
            // contract-validation path prints nothing. An "Ok" message here used to leak into
            // any parallel test that had redirected Console.Error via CliTestHarness.
            throw new CliExitException(string.Empty) { ExitCode = 0 };
        }

        public override string ToString() => typeof(CliContractValidator<T>).ToString();
    }
}
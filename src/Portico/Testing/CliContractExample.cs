namespace Portico.Testing;

/// <summary>
/// A single <c>[CliCommandExample]</c> and whether it matched a route when run through the
/// contract's <see cref="System.Reflection.DispatchProxy"/> application. Returned by
/// <see cref="CliContractValidator{T}.Enumerate"/> as a plain descriptor so a test can turn each
/// example into its own test case (an xUnit <c>[Theory]</c>/<c>MemberData</c>, an NUnit
/// <c>TestCaseSource</c>, etc.) — one red/green per example instead of one for the whole contract.
/// Deliberately carries no test-framework type, keeping <c>Portico</c> runner-agnostic.
/// </summary>
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
public sealed record CliContractExample(string Example, string Description, bool Matched);

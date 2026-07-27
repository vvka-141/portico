using System.Collections.Generic;

namespace Portico.Testing;

/// <summary>
/// A single <c>[CliCommandExample]</c> and what happened when it was run through the contract's
/// <see cref="System.Reflection.DispatchProxy"/> application. Returned by
/// <see cref="CliContractValidator{T}.Enumerate"/> as a plain descriptor so a test can turn each
/// example into its own test case (an xUnit <c>[Theory]</c>/<c>MemberData</c>, an NUnit
/// <c>TestCaseSource</c>, etc.) — one red/green per example instead of one for the whole contract.
/// Deliberately carries no test-framework type, keeping <c>Portico</c> runner-agnostic.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Matched"/> is <see langword="true"/> only when the example dispatched to <strong>the
/// method it is declared on</strong>. An example on method A that routes to method B reports
/// <see cref="Matched"/> = <see langword="false"/> with a <see cref="FailureReason"/> naming the
/// mismatch. To pin the full contract — values and types, not just the route — assert on
/// <see cref="Arguments"/> as well.
/// </para>
/// </remarks>
/// <param name="Example">The verbatim example invocation, less the executable name.</param>
/// <param name="Description">The example's optional one-line description.</param>
/// <param name="Matched">Whether the example dispatched to the method it is declared on.</param>
/// <param name="Handler">
/// The name of the contract method the example dispatched to, or <see langword="null"/> if it
/// dispatched to nothing. Compare against <c>nameof(IMyCommands.Seed)</c> to pin the route.
/// </param>
/// <param name="Arguments">
/// The values the framework bound to the handler's parameters, keyed by parameter name. Empty when
/// nothing dispatched. A parameter the user did not supply appears with its default.
/// </param>
/// <param name="FailureReason">
/// Why the example failed — the framework's own diagnostic (<c>"Unrecognized option(s):
/// --bogus"</c>), a handler-mismatch explanation, or <see langword="null"/> when it succeeded.
/// Put it in the assertion message: a red test that says only <em>that</em> an example broke makes
/// the reader go and find the <em>why</em> the framework already knew.
/// </param>
/// <example><code>
/// public static IEnumerable&lt;object[]&gt; Examples() =&gt;
///     new CliContractValidator&lt;IMyCommands&gt;().Enumerate()
///         .Select(e =&gt; new object[] { e });
///
/// [Theory]
/// [MemberData(nameof(Examples))]
/// public void Example_dispatches(CliContractExample e) =&gt;
///     Assert.True(e.Matched, $"Example did not dispatch: {e.Example}\n  Reason: {e.FailureReason}");
///
/// [Fact]
/// public void Seed_example_binds_the_row_count()
/// {
///     var e = new CliContractValidator&lt;IMyCommands&gt;().Enumerate()
///         .Single(x =&gt; x.Example == "db seed --rows 100");
///
///     Assert.Equal(nameof(IMyCommands.Seed), e.Handler);
///     Assert.Equal(100, e.Arguments["rows"]);
/// }
/// </code></example>
public sealed record CliContractExample(
    string Example,
    string Description,
    bool Matched,
    string? Handler,
    IReadOnlyDictionary<string, object?> Arguments,
    string? FailureReason);

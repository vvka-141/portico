using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Portico.Testing;

// Guards against cross-test Console.Out / Console.Error / Console.In interleaving. xUnit runs
// tests within a single class serially by default; classes run in parallel. The semaphore
// serializes harness runs across the whole AppDomain so any parallel test class using the
// harness gets consistent captures. Tests that deliberately run in parallel should isolate
// themselves with xUnit's [CollectionDefinition(DisableParallelization = true)].

/// <summary>
/// Hermetic test helper for Portico CLIs. Each <see cref="Run(string, string, CancellationToken)"/>
/// call constructs a fresh <see cref="CliApplication"/> with a dedicated in-memory
/// <see cref="ICliConsole"/>, so assertions about exit codes and captured output are free of
/// cross-test global state (unlike <c>Console.SetOut</c> / <c>Console.SetError</c>, which are
/// process-wide).
/// </summary>
/// <remarks>
/// <para>
/// Typical usage:
/// </para>
/// <code>
/// var harness = CliTestHarness.ForApplication(cfg =&gt; cfg.AddCommands(new MyService()));
///
/// harness.Run("myapp init ./project")
///        .ExpectExit(0)
///        .ExpectOut("Initialized");
///
/// harness.Run("myapp init")
///        .ExpectExit(2)
///        .ExpectError("Missing positional argument");
///
/// // Interactive handlers (CliPrompt.GetYesNoAnswer, Console.ReadLine, etc.) — feed input:
/// harness.Run("myapp delete repo", input: "y\n")
///        .ExpectExit(0);
/// </code>
/// <para>
/// The harness overrides <see cref="ICliApplicationBuilder.WithConsole"/>, so any
/// <c>WithConsole</c> call inside the caller's configure delegate is replaced by the
/// harness-owned capturing console.
/// </para>
/// </remarks>
public sealed class CliTestHarness
{
    private static readonly SemaphoreSlim ConsoleGate = new(1, 1);

    private readonly Action<ICliApplicationBuilder> _configure;

    private CliTestHarness(Action<ICliApplicationBuilder> configure)
    {
        _configure = configure;
    }

    /// <summary>
    /// Creates a harness that, on every <c>Run</c> call, rebuilds a <see cref="CliApplication"/>
    /// by applying <paramref name="configure"/> then injecting a capturing <see cref="ICliConsole"/>.
    /// </summary>
    /// <example><code>
    /// var harness = CliTestHarness.ForApplication(cfg =&gt; cfg.AddCommands(new MyService()));
    /// harness.Run("myapp init ./project").ExpectExit(0).ExpectOut("Initialized");
    /// </code></example>
    public static CliTestHarness ForApplication(Action<ICliApplicationBuilder> configure)
    {
        ThrowIf.ArgumentNull(configure);
        return new CliTestHarness(configure);
    }

    /// <summary>
    /// Tokenizes and dispatches a command line. Pass <paramref name="input"/> to feed
    /// standard-input to interactive handlers (e.g. <see cref="CliPrompt.GetYesNoAnswer(string)"/>).
    /// </summary>
    /// <example><code>
    /// harness.Run("myapp delete repo", input: "y\n").ExpectExit(0);
    /// </code></example>
    public CliTestRunResult Run(string commandLine, string? input = null, CancellationToken cancellationToken = default)
    {
        ThrowIf.ArgumentNullOrWhiteSpace(commandLine);
        return RunAsync(commandLine, input, cancellationToken).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Dispatches a <c>Main</c>-shape argv array (no executable-name element at index 0). Matches
    /// <see cref="CliApplication.Run(string[])"/>'s semantics so tests exercise the same path as
    /// a real <c>public static int Main(string[] args)</c> entry point.
    /// </summary>
    /// <example><code>
    /// harness.Run(new[] { "init", "./project" }).ExpectExit(0).ExpectNoError();
    /// </code></example>
    public CliTestRunResult Run(string[] args, string? input = null, CancellationToken cancellationToken = default)
    {
        ThrowIf.ArgumentNull(args);
        return RunAsync(args, input, cancellationToken).GetAwaiter().GetResult();
    }

    /// <summary>Async dispatch; useful for tests that exercise the cancellation path.</summary>
    /// <example><code>
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    /// var result = await harness.RunAsync("myapp build", cancellationToken: cts.Token);
    /// result.ExpectExit(0);
    /// </code></example>
    public Task<CliTestRunResult> RunAsync(string commandLine, string? input = null, CancellationToken cancellationToken = default) =>
        RunCapturedAsync(input, (app, ct) => app.RunAsync(commandLine, ct), cancellationToken);

    /// <summary>Async dispatch; useful for tests that exercise the cancellation path.</summary>
    /// <example><code>
    /// var result = await harness.RunAsync(new[] { "build", "--config", "Release" });
    /// result.ExpectExit(0);
    /// </code></example>
    public Task<CliTestRunResult> RunAsync(string[] args, string? input = null, CancellationToken cancellationToken = default) =>
        RunCapturedAsync(input, (app, ct) => app.RunAsync(args, ct), cancellationToken);

    private async Task<CliTestRunResult> RunCapturedAsync(
        string? input,
        Func<CliApplication, CancellationToken, Task<int>> run,
        CancellationToken cancellationToken)
    {
        await ConsoleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalIn = Console.In;
        try
        {
            var console = new CapturedConsole();
            Console.SetOut(console.OutWriter);
            Console.SetError(console.ErrorWriter);
            if (input is not null)
            {
                Console.SetIn(new StringReader(input));
            }

            var app = CliApplication.Create(cfg =>
            {
                _configure(cfg);
                cfg.WithConsole(console);
            });
            var exit = await run(app, cancellationToken).ConfigureAwait(false);
            return new CliTestRunResult(
                exit,
                console.OutWriter.ToString(),
                console.ErrorWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Console.SetIn(originalIn);
            ConsoleGate.Release();
        }
    }

    /// <summary>
    /// Capturing <see cref="ICliConsole"/> used during a harness run. <see cref="Out"/> and
    /// <see cref="Error"/> are buffered; <see cref="In"/> proxies <see cref="Console.In"/> so
    /// tests can feed input via the <c>input</c> parameter on <c>Run</c>. Both the framework's
    /// <see cref="ICliConsole.In"/> path and the handler's <see cref="Console.In"/> path see
    /// the same fed stream.
    /// </summary>
    private sealed class CapturedConsole : ICliConsole
    {
        public StringWriter OutWriter { get; } = new();
        public StringWriter ErrorWriter { get; } = new();
        public TextWriter Out => OutWriter;
        public TextWriter Error => ErrorWriter;
        public TextReader In => Console.In;
    }
}

/// <summary>
/// Result of a single <see cref="CliTestHarness.Run(string, string, CancellationToken)"/>
/// invocation. The fluent <c>Expect…</c> methods throw a descriptive exception on mismatch and
/// return <c>this</c> so assertions can chain; the raw properties are available for frameworks
/// that prefer their own assertion style (xUnit, NUnit, Shouldly, etc.).
/// </summary>
public sealed record CliTestRunResult(int ExitCode, string StandardOut, string StandardError)
{
    /// <summary>Throws if <see cref="ExitCode"/> differs from <paramref name="expected"/>.</summary>
    /// <example><code>
    /// harness.Run("myapp init").ExpectExit(2);
    /// </code></example>
    public CliTestRunResult ExpectExit(int expected)
    {
        if (ExitCode == expected) return this;
        throw new CliTestAssertionException(
            $"Expected exit code {expected} but got {ExitCode}.{Environment.NewLine}" +
            $"--- stdout ---{Environment.NewLine}{StandardOut}{Environment.NewLine}" +
            $"--- stderr ---{Environment.NewLine}{StandardError}");
    }

    /// <summary>Throws if <see cref="StandardOut"/> does not contain <paramref name="substring"/>.</summary>
    /// <example><code>
    /// harness.Run("myapp version").ExpectExit(0).ExpectOut("2.0.0");
    /// </code></example>
    public CliTestRunResult ExpectOut(string substring, StringComparison comparison = StringComparison.Ordinal)
    {
        ThrowIf.ArgumentNull(substring);
        if (StandardOut.Contains(substring, comparison)) return this;
        throw new CliTestAssertionException(
            $"Expected stdout to contain '{substring}' but it did not.{Environment.NewLine}" +
            $"--- stdout ---{Environment.NewLine}{StandardOut}");
    }

    /// <summary>Throws if <see cref="StandardError"/> does not contain <paramref name="substring"/>.</summary>
    /// <example><code>
    /// harness.Run("myapp init").ExpectExit(2).ExpectError("Missing positional argument");
    /// </code></example>
    public CliTestRunResult ExpectError(string substring, StringComparison comparison = StringComparison.Ordinal)
    {
        ThrowIf.ArgumentNull(substring);
        if (StandardError.Contains(substring, comparison)) return this;
        throw new CliTestAssertionException(
            $"Expected stderr to contain '{substring}' but it did not.{Environment.NewLine}" +
            $"--- stderr ---{Environment.NewLine}{StandardError}");
    }

    /// <summary>Throws if <see cref="StandardError"/> has any non-whitespace content.</summary>
    /// <example><code>
    /// harness.Run("myapp status").ExpectExit(0).ExpectNoError();
    /// </code></example>
    public CliTestRunResult ExpectNoError()
    {
        if (string.IsNullOrWhiteSpace(StandardError)) return this;
        throw new CliTestAssertionException(
            $"Expected stderr to be empty but it contained output:{Environment.NewLine}{StandardError}");
    }
}

/// <summary>
/// Thrown when a <see cref="CliTestRunResult"/> fluent assertion fails. Users who prefer
/// a test framework's native assertion types (Assert.Equal, Shouldly, FluentAssertions) can
/// read <see cref="CliTestRunResult.ExitCode"/>/<see cref="CliTestRunResult.StandardOut"/>/
/// <see cref="CliTestRunResult.StandardError"/> directly and skip the Expect methods.
/// </summary>
public sealed class CliTestAssertionException : Exception
{
    /// <summary>Creates the exception with the assertion-failure <paramref name="message"/>.</summary>
    public CliTestAssertionException(string message) : base(message) { }
}

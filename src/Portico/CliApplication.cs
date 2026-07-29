using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Portico.Completion;
using Portico.Reflection;

namespace Portico;

/// <summary>
/// The CLI application — the terminal-side equivalent of <c>WebApplication</c>. Construct via
/// <see cref="Create"/>, configure through <see cref="ICliApplicationBuilder"/>, then invoke
/// <see cref="Run()"/> / <see cref="RunAsync(CancellationToken)"/>.
/// </summary>
public sealed partial class CliApplication
{
    internal const string TrimWarning =
        "Portico discovers routes and binds options via reflection. " +
        "A trimmed or NativeAOT publish will fail at runtime. " +
        "See docs/explanation/aot.md in the Portico repository.";

    private readonly ICliConsole _console;
    private readonly Func<string>? _versionFactory;
    private readonly IReadOnlyList<string>? _versionTriggers;
    private readonly IReadOnlyList<string>? _helpTriggers;
    private readonly bool _helpSuppressed;
    private readonly ImmutableArray<CliAction> _actions;
    private readonly CliShortOptionSchema _shortOptionSchema;
    private readonly HashSet<string> _registeredOptionNames;

    // ---------------------------------------------------------------------------------------
    //  Public entry points
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a CLI application from one or more command contracts. This is the framework's entry
    /// point: register your services with <see cref="ICliApplicationBuilder.AddCommands(object)"/>,
    /// add any middleware or version information, then <see cref="Run(string[])"/> the result.
    /// Routing, binding and help are all derived from the attributes on the registered types —
    /// there is nothing else to configure.
    /// </summary>
    /// <param name="initialize">
    /// Configures the application. Runs once, immediately; the builder is not retained.
    /// </param>
    /// <returns>An immutable application, ready to run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="initialize"/> is null.</exception>
    /// <exception cref="CliConfigurationException">
    /// The registered contracts are not a valid CLI — two methods declare the same route, a route
    /// placeholder names no parameter, an option alias is declared twice, and so on. Thrown here,
    /// at construction, rather than at dispatch: a misconfigured CLI should fail on startup, not on
    /// the one command nobody tested.
    /// </exception>
    /// <example><code>
    /// var app = CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool()));
    /// return app.Run(args);
    /// </code></example>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    [DebuggerStepThrough]
    public static CliApplication Create(Action<ICliApplicationBuilder> initialize)
    {
        ThrowIf.ArgumentNull(initialize);
        var builder = new Builder();
        initialize.Invoke(builder);
        return new CliApplication(builder);
    }

    /// <summary>Synchronous wrapper around <see cref="RunAsync(string, CancellationToken)"/>.</summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool())).Run("greet --name Ada");
    /// </code></example>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    [DebuggerStepThrough]
    public int Run(string commandLine) =>
        RunAsync(commandLine, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Synchronous wrapper around <see cref="RunAsync(string[], CancellationToken)"/>.</summary>
    /// <example><code>
    /// public static int Main(string[] args) =&gt;
    ///     CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool())).Run(args);
    /// </code></example>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    [DebuggerStepThrough]
    public int Run(string[] args) =>
        RunAsync(args, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Synchronous wrapper around <see cref="RunAsync(CancellationToken)"/>.</summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool())).Run();
    /// </code></example>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    [DebuggerStepThrough]
    public int Run() =>
        RunAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Reads the current process's argv via <see cref="Environment.GetCommandLineArgs"/>, dispatches
    /// the matched command, and returns its exit code. If <paramref name="cancellationToken"/> is
    /// not cancellable (the default case), the framework transparently wires
    /// <see cref="Console.CancelKeyPress"/> to a scoped <see cref="CancellationTokenSource"/> so
    /// Ctrl+C (SIGINT) triggers graceful cancellation and POSIX exit 130, and registers a SIGTERM
    /// handler so Docker / Kubernetes graceful shutdown cancels the same token (POSIX exit 143)
    /// instead of running until SIGKILL. Pass your own cancellable token to opt out (e.g. when
    /// composing with a parent scope or a wall-clock timeout).
    /// </summary>
    /// <example><code>
    /// return await CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool())).RunAsync();
    /// </code></example>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    [DebuggerStepThrough]
    public Task<int> RunAsync(CancellationToken cancellationToken = default) =>
        RunWithAutoCancelAsync(CliInvocation.ProcessArgv(), cancellationToken);

    /// <summary>
    /// Parses a whitespace-separated command line and dispatches it. Auto-wires Ctrl+C when
    /// <paramref name="cancellationToken"/> isn't cancellable — see
    /// <see cref="RunAsync(CancellationToken)"/>.
    /// </summary>
    /// <example><code>
    /// await CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool())).RunAsync("greet --name Ada");
    /// </code></example>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    [DebuggerStepThrough]
    public Task<int> RunAsync(string commandLine, CancellationToken cancellationToken = default) =>
        RunWithAutoCancelAsync(CliInvocation.TokenizeFromString(commandLine), cancellationToken);

    /// <summary>
    /// Dispatches the <c>args</c> array from <c>Main(string[] args)</c> — which by C# convention
    /// <strong>omits</strong> the executable name. The framework prepends the current process's
    /// executable name (<see cref="Environment.GetCommandLineArgs"/>[0]) automatically so help
    /// rendering shows the right name. Auto-wires Ctrl+C when
    /// <paramref name="cancellationToken"/> isn't cancellable — see
    /// <see cref="RunAsync(CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    /// Call this from your <c>Main</c>: <c>public static int Main(string[] args) =&gt;
    /// CliApplication.Create(...).Run(args);</c>. For programmatic dispatch where your argv
    /// already includes the exe name (matching <see cref="Environment.GetCommandLineArgs"/>'s
    /// shape), call <see cref="RunAsync(CancellationToken)"/> parameterless.
    /// </remarks>
    /// <example><code>
    /// public static Task&lt;int&gt; Main(string[] args) =&gt;
    ///     CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool())).RunAsync(args);
    /// </code></example>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    [DebuggerStepThrough]
    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        ThrowIf.ArgumentNull(args);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is null)
            {
                throw new ArgumentException(
                    $"args[{i}] is null. The argv array passed to RunAsync must contain only non-null " +
                    $"strings (an empty string is fine; null is not). This is typically a programmer " +
                    $"error — check how you constructed the array before dispatching.",
                    nameof(args));
            }
        }
        // The name the user typed ("myapp"), not the managed assembly path ("myapp.dll") that
        // Environment.GetCommandLineArgs()[0] hands back for an apphost-launched app.
        var full = new string[args.Length + 1];
        full[0] = CliInvocation.ProcessExecutableName();
        Array.Copy(args, 0, full, 1, args.Length);
        return RunWithAutoCancelAsync(full, cancellationToken);
    }

    /// <summary>
    /// Chooses between user-supplied cancellation (trusts the caller) and auto-wired Ctrl+C
    /// (the "just works" default). Detection: if <paramref name="cancellationToken"/> is
    /// <see cref="CancellationToken.CanBeCanceled"/>, the caller is managing lifetime — use
    /// their token directly. Otherwise attach a scoped <see cref="Console.CancelKeyPress"/>
    /// handler for the duration of the run and detach in a finally.
    /// </summary>
    private async Task<int> RunWithAutoCancelAsync(string[] argvWithExe, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
        {
            return await DispatchAsync(argvWithExe, cancellationToken).ConfigureAwait(false);
        }

        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += handler;

        // SIGTERM is the graceful-shutdown signal Docker / Kubernetes send before SIGKILL.
        // Mirror the Ctrl+C (SIGINT) auto-cancel path so a containerized CLI drains its
        // handler's CancellationToken on pod termination instead of being force-killed.
        var terminated = new StrongBox<bool>(false);
        var sigterm = TryRegisterSigterm(cts, terminated);
        try
        {
            var exitCode = await DispatchAsync(argvWithExe, cts.Token).ConfigureAwait(false);

            // If cancellation was SIGTERM-driven, report the SIGTERM POSIX exit code (143)
            // instead of the SIGINT code (130) the cancellation path produced by default.
            if (terminated.Value && exitCode == CliExitException.CancelledExitCode)
            {
                return CliExitException.TerminatedExitCode;
            }
            return exitCode;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            sigterm?.Dispose();
        }
    }

    /// <summary>
    /// Registers a SIGTERM handler that mirrors the SIGINT (Ctrl+C) auto-cancel path. Returns the
    /// registration (disposed by the caller alongside the <see cref="Console.CancelKeyPress"/>
    /// detach) or <c>null</c> when the platform does not support POSIX signal registration. The
    /// <see cref="PlatformNotSupportedException"/> is routed to <see cref="Trace"/> like other
    /// framework-internal faults so it never crashes the run.
    /// </summary>
    private static IDisposable? TryRegisterSigterm(CancellationTokenSource cts, StrongBox<bool> terminated)
    {
        try
        {
            return PosixSignalRegistration.Create(
                PosixSignal.SIGTERM,
                ctx => OnPosixTermination(ctx, cts, terminated));
        }
        catch (PlatformNotSupportedException e)
        {
            Trace.TraceError(e.ToString());
            return null;
        }
    }

    /// <summary>
    /// SIGTERM handler body — extracted as an internal seam so the auto-cancel wiring is unit
    /// testable without raising a real OS signal (which isn't portably reproducible in-process on
    /// Windows CI). Mirrors the ConsoleLifetime pattern: set <see cref="PosixSignalContext.Cancel"/>
    /// to suppress the runtime's default immediate termination, record that termination was
    /// SIGTERM-driven (so the run maps to <see cref="CliExitException.TerminatedExitCode"/>), then
    /// cancel the scoped CTS so the in-flight handler's <see cref="CancellationToken"/> trips.
    /// </summary>
    internal static void OnPosixTermination(
        PosixSignalContext context,
        CancellationTokenSource cts,
        StrongBox<bool> terminated)
    {
        context.Cancel = true;
        terminated.Value = true;
        cts.Cancel();
    }

    /// <summary>
    /// Internal dispatch — expects argv in <see cref="Environment.GetCommandLineArgs"/> shape
    /// (executable name at index 0). Every public <c>RunAsync</c> overload normalizes into this
    /// shape and runs through <see cref="RunWithAutoCancelAsync"/> before calling here.
    /// </summary>
    [DebuggerStepThrough]
    private Task<int> DispatchAsync(string[] argvWithExe, CancellationToken cancellationToken) =>
        SafeRunAsync(CliInvocation.FromArgs(PreprocessArgs(argvWithExe)), cancellationToken);

    /// <summary>
    /// Returns the canonical route signature of every registered command (literal segments verbatim,
    /// argument slots rendered as <c>{argName}</c>).
    /// </summary>
    /// <example><code>
    /// var app = CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool()));
    /// foreach (var sig in app.GetRouteSignatures()) Console.WriteLine(sig); // e.g. "greet {name}"
    /// </code></example>
    public IReadOnlyList<string> GetRouteSignatures() =>
        _actions.Select(a => a.RouteSignature).ToArray();

    /// <summary>
    /// Emits a shell-completion script for this application's registered commands into
    /// <paramref name="output"/>. See <see cref="CliCompletion"/> for scope and semantics.
    /// </summary>
    /// <example><code>
    /// var app = CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool()));
    /// app.EmitCompletion(CliCompletionShell.Bash, "mytool", Console.Out);
    /// </code></example>
    public void EmitCompletion(CliCompletionShell shell, string executableName, TextWriter output) =>
        CliCompletion.Emit(shell, executableName, GetRouteSignatures(), output);

    // ---------------------------------------------------------------------------------------
    //  Construction
    // ---------------------------------------------------------------------------------------

    private CliApplication(Builder config)
    {
        _console = config.Console ?? SystemCliConsole.Instance;
        _versionFactory = config.VersionFactory;
        _versionTriggers = config.VersionTriggers;
        _helpTriggers = config.HelpTriggers;
        _helpSuppressed = config.HelpSuppressed;

        var globalOptions = config.Middleware.Distinct().ToArray();
        RejectDuplicateGlobalOptionAliases(globalOptions);
        _actions =
            [
                ..config.Services
                    .SelectMany(service =>
                    {
                        var context = new CliContext(service.RootRoutes, globalOptions, _console);
                        return CliMethodInfo
                            .Get(service.ServiceType, context)
                            .Select(method => new CliAction(
                                method,
                                service.InstanceFactory,
                                service.Release,
                                _console));
                    })
            ];

        var duplicate = _actions
            .GroupBy(a => a.RouteSignature, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            var offenders = duplicate.ToArray();
            throw CliConfigurationException.DuplicateRoute(
                duplicate.Key,
                offenders[0].MethodDescription,
                offenders[1].MethodDescription);
        }

        (_shortOptionSchema, _registeredOptionNames) = BuildShortOptionSchema(_actions);

        WarnAboutShadowedBuiltInTriggers(_actions, _helpTriggers, _versionTriggers);
    }

    /// <summary>
    /// Tells the author when a route has claimed a help or version trigger as one of its own option
    /// aliases, which stops the framework answering that token <em>for that command</em>.
    /// </summary>
    /// <remarks>
    /// The precedence itself is deliberate and stays (SOL-75): a route's own option beats the
    /// built-in, which is what lets <c>-h</c> mean <c>--host</c>. What was missing is that the author
    /// got no signal, and the user got a type error about the word "help" with nothing pointing at
    /// the cause (POR-120).
    /// <para>
    /// A trace warning rather than an exception, for the same reason as the short-option arity
    /// conflict: the shape is legal and occasionally intended, so failing the build would reject a
    /// working program. And it is computed against the <em>effective</em> triggers — an application
    /// that replaced them with <c>WithHelp(h => h.Triggers(...))</c> is measured against its own set, which is
    /// something a Roslyn analyzer could not do, since the configuration is a runtime call.
    /// </para>
    /// <para>
    /// The message names only the triggers actually shadowed. A route declaring <c>-h</c> leaves
    /// <c>--help</c> working, and claiming otherwise would be the same overreach the ticket was
    /// filed about.
    /// </para>
    /// </remarks>
    private static void WarnAboutShadowedBuiltInTriggers(
        IReadOnlyList<CliAction> actions,
        IReadOnlyList<string>? helpTriggers,
        IReadOnlyList<string>? versionTriggers)
    {
        Warn("help", helpTriggers ?? CliBuiltInTriggers.OptionFormHelpTriggers);
        Warn("version", versionTriggers ?? CliBuiltInTriggers.OptionFormVersionTriggers);

        void Warn(string kind, IReadOnlyList<string> triggers)
        {
            var optionForms = triggers.Where(t => !string.IsNullOrEmpty(t) && t[0] == '-').ToArray();
            if (optionForms.Length == 0) return;

            foreach (var action in actions)
            {
                var shadowed = optionForms.Where(action.DeclaresOptionAlias).ToArray();
                if (shadowed.Length == 0) continue;

                var remaining = optionForms.Except(shadowed, CliAliasComparer.Instance).ToArray();

                Trace.TraceWarning(
                    $"Route '{action.RouteSignature}' declares {shadowed.Select(t => $"'{t}'").Join(", ")} " +
                    $"as its own option{(shadowed.Length == 1 ? "" : "s")}, so Portico's built-in {kind} " +
                    $"no longer answers {(shadowed.Length == 1 ? "it" : "them")} for that command. " +
                    (remaining.Length > 0
                        ? $"{remaining.Select(t => $"'{t}'").Join(", ")} still {(remaining.Length == 1 ? "works" : "work")}."
                        : $"That command has no remaining way to show its {kind}.") +
                    " This is deliberate — a route's own option wins over a built-in trigger — but it is " +
                    "worth knowing before a user finds it.");
            }
        }
    }

    private static void RejectDuplicateGlobalOptionAliases(IEnumerable<CliMiddleware> middleware)
    {
        var seen = new Dictionary<string, string>(CliAliasComparer.Instance);

        foreach (var bundle in middleware)
        {
            foreach (var option in bundle.GetOptions())
            {
                foreach (var alias in option.Aliases)
                {
                    var origin = $"global option '{option.Name}' on middleware '{bundle.GetType().FullName}'";
                    if (seen.TryGetValue(alias, out var existing))
                    {
                        throw new CliConfigurationException(
                            $"Global option alias '{alias}' is declared by both {existing} and {origin}. " +
                            "Each global option alias must be unique across the application.");
                    }

                    seen[alias] = origin;
                }
            }
        }
    }

    // Shared with ICliApplicationBuilder.WithVersion() — see CliVersionDiscovery (SOL-82).
    private static readonly Func<string> DefaultVersionFactory = CliVersionDiscovery.FromEntryAssembly;

    // ---------------------------------------------------------------------------------------
    //  Dispatch pipeline
    // ---------------------------------------------------------------------------------------

    private string[] PreprocessArgs(string[] args)
    {
        if (args.Length <= 1 || _shortOptionSchema.IsEmpty) return args;

        var tail = new string[args.Length - 1];
        Array.Copy(args, 1, tail, 0, tail.Length);

        var expanded = CliShortOptionExpander.Expand(tail, _shortOptionSchema, _registeredOptionNames);
        if (ReferenceEquals(expanded, tail)) return args;

        var merged = new string[expanded.Length + 1];
        merged[0] = args[0];
        Array.Copy(expanded, 0, merged, 1, expanded.Length);
        return merged;
    }

    private async Task<int> SafeRunAsync(CliInvocation invocation, CancellationToken cancellationToken)
    {
        try
        {
            return await RunCoreAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (CliExitException e)
        {
            if (!string.IsNullOrWhiteSpace(e.Message)) _console.Error.WriteLine(e.Message);
            return e.ExitCode;
        }
        catch (OperationCanceledException)
        {
            _console.Error.WriteLine("Operation cancelled.");
            return CliExitException.CancelledExitCode;
        }
        catch (Exception e)
        {
            Trace.TraceError(e.ToString());
            _console.Error.WriteLine($"Unhandled error: {e.Message}");
            return CliExitException.RuntimeErrorExitCode;
        }
    }

    private async Task<int> RunCoreAsync(CliInvocation invocation, CancellationToken cancellationToken)
    {
        var actions = Match(invocation);
        if (actions.Length == 1)
        {
            // A declared route option wins over the built-in help/version triggers (SOL-75): if the
            // matched command declares the trigger token (e.g. `-h` for `--host`, or an explicit
            // `-V`) as one of its own option aliases, the token binds to the command instead of
            // firing the built-in path. Triggers the route does NOT declare still fire, so
            // per-command `--help` and a global `--version` keep working on ordinary routes.
            var action = actions[0];
            if (IsVersionRequested(invocation, action) && _versionFactory is not null)
            {
                _console.Out.WriteLine(_versionFactory() ?? string.Empty);
                return CliExitException.SuccessExitCode;
            }
            if (IsHelpRequested(invocation, action))
            {
                action.ShowHelp(invocation);
                return CliExitException.SuccessExitCode;
            }
            return await action.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        }

        // No single matched route — the trigger cannot belong to a command, so the built-in
        // help/version paths fire unqualified (top-level `--version`, `--help`, `version`, `/?`, …).
        if (IsVersionRequested(invocation) && _versionFactory is not null)
        {
            _console.Out.WriteLine(_versionFactory() ?? string.Empty);
            return CliExitException.SuccessExitCode;
        }

        if (actions.Length > 1)
        {
            throw CliExitException.AmbiguousCommand(actions.Select(a => a.ToString()));
        }

        // Help-only second pass — matches on literal route prefix so `myapp init --help` works
        // without the user supplying the placeholder argument, and `myapp help init` (the git
        // convention) shows `init`'s help by stripping the leading `help` subcommand.
        if (IsHelpRequested(invocation))
        {
            var helpMatches = MatchForHelp(HelpTargetSegments(invocation));
            if (helpMatches.Length == 1)
            {
                helpMatches[0].ShowHelp(invocation);
                return CliExitException.SuccessExitCode;
            }
            DisplayGeneralHelp(invocation);
            return CliExitException.SuccessExitCode;
        }

        if (invocation.Segments.Length == 0 && invocation.Options.Length == 0)
        {
            DisplayGeneralHelp(invocation);
            return CliExitException.SuccessExitCode;
        }

        OnActionNotFound(invocation);
        return CliExitException.UsageErrorExitCode;
    }

    private CliAction[] Match(CliInvocation invocation)
    {
        var matches = _actions.Where(a => a.IsMatch(invocation)).ToList();
        if (matches.Count == 0) return [];

        return matches
            .GroupBy(a => Math.Round(a.RankByOptions(invocation), 3))
            .OrderByDescending(g => g.Key)
            .Take(1)
            .SelectMany(g => g)
            .ToArray();
    }

    private CliAction[] MatchForHelp(IReadOnlyList<string> segments) =>
        _actions
            .Where(a =>
            {
                var literals = a.LiteralPrefix;
                if (literals.Count != segments.Count) return false;
                for (int i = 0; i < literals.Count; i++)
                {
                    if (!string.Equals(literals[i], segments[i], StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                return true;
            })
            .ToArray();

    /// <summary>
    /// The segments a help request targets. A leading bare <c>help</c> subcommand (the
    /// <c>git help &lt;cmd&gt;</c> convention) is stripped so <c>app help init</c> resolves to
    /// <c>init</c>'s help; a lone <c>app help</c> is left untouched and falls through to general help.
    /// </summary>
    private IReadOnlyList<string> HelpTargetSegments(CliInvocation invocation)
    {
        var segments = invocation.Segments;
        return segments.Length >= 2 && IsHelpSubcommandToken(segments[0])
            ? segments.Skip(1).ToArray()
            : segments;
    }

    /// <summary>True when <paramref name="token"/> is a bare (non-option) help trigger word.</summary>
    private bool IsHelpSubcommandToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token[0] == '-') return false;
        return _helpTriggers is not null
            ? _helpTriggers.Any(t => !string.IsNullOrEmpty(t) && t[0] != '-'
                && string.Equals(t, token, StringComparison.OrdinalIgnoreCase))
            : DefaultHelpSignalRegex().IsMatch(token);
    }

    // Top-level help: what commands exist, not what every option of every command is (POR-31).
    private void DisplayGeneralHelp(CliInvocation invocation) =>
        _console.Out.WriteLine(
            Reflection.CliHelpRenderer.RenderOverview(
                [.. _actions.Select(a => a.RouteModel)],
                invocation.ExecutableName));

    private void OnActionNotFound(CliInvocation invocation)
    {
        var nearMisses = FindNearMisses(invocation);
        if (nearMisses.Count > 0)
        {
            ReportShapeMismatch(invocation, nearMisses);
            return;
        }

        // Deliberately renders the executable and route segments ONLY — never the option values.
        // No route matched, so there is no option metadata and the framework cannot know which
        // values are secrets. Echoing them would put a --connection-string or --token into stderr,
        // which in a container is the log stream. The values add nothing to a "did you mean"
        // diagnostic anyway; the segments are what the user mistyped.
        var typed = invocation.Segments
            .Prepend(invocation.ExecutableName)
            .Select(token => token.HasWhiteSpaces() ? token.Quote() : token)
            .Join(" ");
        var header = $"Unknown command: {CliSanitizer.Sanitize(typed)}.";
        var nearMatchSignatures = new HashSet<string>(
            nearMisses.Select(a => a.RouteSignature), StringComparer.OrdinalIgnoreCase);
        var suggestions = GetSuggestions(invocation, nearMatchSignatures).ToArray();
        if (suggestions.Length == 0)
        {
            _console.Error.WriteLine($"{header} Run with --help to list available commands.");
            return;
        }

        var lines = new List<string> { header, "Did you mean:" };
        lines.AddRange(suggestions.Select(s => "  " + s));
        lines.Add("Run with --help for the full command list.");
        _console.Error.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private List<CliAction> FindNearMisses(CliInvocation invocation)
    {
        if (invocation.Segments.Length == 0) return [];

        var result = new List<CliAction>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in _actions)
        {
            if (!seen.Add(action.RouteSignature)) continue;

            var literals = action.LiteralPrefix;
            if (literals.Count == 0 || invocation.Segments.Length < literals.Count) continue;

            var prefixMatches = true;
            for (int i = 0; i < literals.Count; i++)
            {
                if (!string.Equals(literals[i], invocation.Segments[i], StringComparison.OrdinalIgnoreCase))
                {
                    prefixMatches = false;
                    break;
                }
            }
            if (!prefixMatches) continue;

            var model = action.RouteModel;
            if (invocation.Segments.Length >= model.MinSegmentCount &&
                invocation.Segments.Length <= model.Segments.Length)
            {
                continue;
            }

            result.Add(action);
        }

        return result;
    }

    private void ReportShapeMismatch(CliInvocation invocation, List<CliAction> nearMisses)
    {
        var best = nearMisses[0];
        var model = best.RouteModel;
        var prefix = best.LiteralPrefix.Count;

        // LiteralPrefix stops at the first placeholder, so subtracting it leaves the placeholders
        // *and* every literal that follows them. Counting that as an argument count overstates it:
        // in 'worker {id} drain' the user supplies one value and types one keyword. Only when the
        // whole tail is placeholders does "argument" describe what is missing.
        var tailIsAllArguments = true;
        for (int i = prefix; i < model.Segments.Length; i++)
        {
            if (model.Segments[i] is not CliArgumentSegment)
            {
                tailIsAllArguments = false;
                break;
            }
        }

        // And when it is not, no argument count can be right: 'worker w-42' supplies exactly the
        // one argument the route declares and is still wrong, because 'drain' is missing. The
        // mismatch is in the shape, so the whole route is what gets counted.
        var (noun, max, min, supplied) = tailIsAllArguments
            ? ("argument",
               model.Segments.Length - prefix,
               model.MinSegmentCount - prefix,
               invocation.Segments.Length - prefix)
            : ("segment",
               model.Segments.Length,
               model.MinSegmentCount,
               invocation.Segments.Length);

        var expected = min == max ? $"{max}" : $"{min}–{max}";

        var lines = new List<string>
        {
            $"Command '{best.RouteSignature}' expects {expected} " +
            $"{noun}{(max == 1 ? "" : "s")}, got {supplied}."
        };

        var unrecognized = invocation.Options
            .Where(opt => !best.DeclaresOptionAlias(opt.Name))
            .Select(opt => opt.Name)
            .ToList();

        if (unrecognized.Count > 0 && invocation.Segments.Length < model.MinSegmentCount)
        {
            var names = unrecognized.Select(n => $"'{n}'").Join(", ");
            lines.Add(
                $"If {names} {(unrecognized.Count == 1 ? "is" : "are")} " +
                $"meant as positional, pass {(unrecognized.Count == 1 ? "it" : "them")} " +
                $"after the '--' terminator (e.g. '… -- {unrecognized[0].TrimStart('-')}').");
        }

        _console.Error.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private IEnumerable<string> GetSuggestions(
        CliInvocation invocation,
        IReadOnlySet<string>? exclude = null)
    {
        if (invocation.Segments.Length == 0) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return _actions
            .Where(a => seen.Add(a.RouteSignature))
            .Where(a => exclude is null || !exclude.Contains(a.RouteSignature))
            .Select(action =>
            {
                var literals = action.LiteralPrefix;
                if (literals.Count == 0) return (Signature: action.RouteSignature, Distance: int.MaxValue);
                var literal = string.Join(" ", literals);
                var userPrefix = string.Join(" ", invocation.Segments.AsEnumerable().Take(literals.Count));
                var tolerance = Math.Max(2, literal.Length / 3);
                var distance = CliFuzzyMatch.LevenshteinDistance(userPrefix, literal);
                return (
                    Signature: action.RouteSignature,
                    Distance: distance <= tolerance ? distance : int.MaxValue);
            })
            .Where(c => c.Distance != int.MaxValue)
            .OrderBy(c => c.Distance)
            .Take(3)
            .Select(c => c.Signature)
            .ToArray();
    }

    // ---------------------------------------------------------------------------------------
    //  Help / version detection
    // ---------------------------------------------------------------------------------------

    // The default trigger patterns live on CliBuiltInTriggers so the option materializer can consult
    // the same definition when explaining a shadowed trigger (POR-120). Two copies would drift.
    private static Regex DefaultHelpSignalRegex() => CliBuiltInTriggers.HelpSignal();

    private static Regex DefaultVersionSignalRegex() => CliBuiltInTriggers.VersionSignal();

    private bool IsVersionRequested(CliInvocation invocation, CliAction? matchedRoute = null)
    {
        if (_versionTriggers is not null) return MatchesAnyTrigger(invocation, _versionTriggers, matchedRoute);
        foreach (var option in invocation.Options)
        {
            if (DefaultVersionSignalRegex().IsMatch(option.Name) && !DeclaredByRoute(matchedRoute, option.Name))
                return true;
        }
        return false;
    }

    private bool IsHelpRequested(CliInvocation invocation, CliAction? matchedRoute = null)
    {
        if (_helpSuppressed) return false;
        if (_helpTriggers is not null) return MatchesAnyTrigger(invocation, _helpTriggers, matchedRoute);

        // When a route matched, a help-looking *segment* is one of that route's argument values —
        // the route could not have matched otherwise — so only option-form triggers may fire.
        if (matchedRoute is null)
        {
            foreach (var segment in invocation.Segments)
            {
                if (DefaultHelpSignalRegex().IsMatch(segment)) return true;
            }
        }
        foreach (var option in invocation.Options)
        {
            if (DefaultHelpSignalRegex().IsMatch(option.Name) && !DeclaredByRoute(matchedRoute, option.Name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the matched route declares <paramref name="token"/> as one of its own option
    /// aliases, in which case the built-in help/version trigger yields and the token binds to the
    /// command. Alias comparison mirrors <see cref="MatchesAnyTrigger"/>: single-char short options
    /// (<c>-x</c>) are case-sensitive so the <c>-V</c>/<c>-v</c> (version/verbose) convention is
    /// preserved; longer forms are case-insensitive.
    /// </summary>
    private static bool DeclaredByRoute(CliAction? matchedRoute, string token) =>
        matchedRoute is not null && matchedRoute.DeclaresOptionAlias(token);

    /// <summary>
    /// Trigger matching for customized help / version triggers. A trigger starting with <c>-</c>
    /// is an option form (matched against <see cref="CliInvocation.Options"/>); otherwise it's
    /// a subcommand form (matched against the first positional segment so <c>myapp foo version</c>
    /// doesn't accidentally hijack). Single-char short options are case-sensitive (preserving
    /// the <c>-V</c>-not-<c>-v</c> convention); longer forms are case-insensitive.
    /// </summary>
    private static bool MatchesAnyTrigger(
        CliInvocation invocation,
        IReadOnlyList<string> triggers,
        CliAction? matchedRoute = null)
    {
        foreach (var trigger in triggers)
        {
            if (string.IsNullOrEmpty(trigger)) continue;
            if (trigger[0] == '-')
            {
                // A route that declares this alias as its own option wins over the trigger (SOL-75).
                if (DeclaredByRoute(matchedRoute, trigger)) continue;
                foreach (var option in invocation.Options)
                {
                    if (CliAliasComparer.Instance.Equals(option.Name, trigger)) return true;
                }
            }
            else if (matchedRoute is null &&
                     invocation.Segments.Length > 0 &&
                     string.Equals(invocation.Segments[0], trigger, StringComparison.OrdinalIgnoreCase))
            {
                // Segment-form triggers only fire when no route matched — a matched route has
                // already consumed its leading segment as a literal or argument value.
                return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------------------------------
    //  Short-option arity schema
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The application-wide char → arity map the POSIX preprocessor splits clusters with, plus every
    /// registered option name.
    /// </summary>
    /// <remarks>
    /// <b>The schema is application-wide, and it has to be.</b> Expansion runs on raw argv before any
    /// route has matched — <c>-fx</c> must be split before the parser can know which command it
    /// belongs to — so there is no route whose schema could be consulted.
    /// <para>
    /// The consequence is real: when two commands declare the same short letter with different
    /// arities, the letter is dropped from the schema and <b>bundling stops working for it
    /// everywhere</b>, including on a command that never had a conflict. That is deliberate — the
    /// expander refuses to guess rather than split a cluster the wrong way — but it used to be
    /// entirely silent, so an author had no way to learn that registering one command degraded
    /// another (POR-119).
    /// </para>
    /// <para>
    /// It is reported as a <b>trace warning, not a <see cref="CliConfigurationException"/></b>. Two
    /// independently-developed tools composed into one binary may each legitimately use <c>-f</c>
    /// with a different arity, and that is a composition <c>docs/how-to/compose-clis.md</c> actively
    /// promotes; throwing would fail a program that works, to prevent a degradation the user can
    /// resolve by typing <c>-f -x</c> instead of <c>-fx</c>. Both commands keep working unbundled.
    /// </para>
    /// </remarks>
    private static (CliShortOptionSchema, HashSet<string>) BuildShortOptionSchema(IEnumerable<CliAction> actions)
    {
        var arity = new Dictionary<char, CliShortOptionArity>();
        var registered = new HashSet<string>(StringComparer.Ordinal);
        var declaredBy = new Dictionary<char, string>();
        var conflicting = new Dictionary<char, string>();

        foreach (var action in actions)
        {
            foreach (var info in action.GetOptionInfos())
            {
                foreach (var alias in info.Aliases) registered.Add(alias);

                foreach (var shortChar in info.Aliases
                             .Where(a => a.Length == 2 && a[0] == '-' && a[1] != '-')
                             .Select(a => a[1]))
                {
                    var thisArity =
                        info.IsFlagArity ? CliShortOptionArity.Flag :
                        info.IsMapArity ? CliShortOptionArity.Map :
                        CliShortOptionArity.Scalar;

                    if (arity.TryGetValue(shortChar, out var existing))
                    {
                        if (existing != thisArity && !conflicting.ContainsKey(shortChar))
                        {
                            conflicting[shortChar] =
                                $"'-{shortChar}' is declared as {Describe(existing)} by route " +
                                $"'{declaredBy[shortChar]}' and as {Describe(thisArity)} by route " +
                                $"'{action.RouteSignature}'. Portico cannot know which a bundled token " +
                                $"like '-{shortChar}x' means, so short-option bundling is disabled for " +
                                $"'-{shortChar}' across the whole application — including on commands " +
                                $"that declare it consistently. Both options still work when written " +
                                $"separately ('-{shortChar} -x'). Give one of them a different letter " +
                                $"to restore bundling.";
                        }
                    }
                    else
                    {
                        arity[shortChar] = thisArity;
                        declaredBy[shortChar] = action.RouteSignature;
                    }
                }
            }
        }

        foreach (var (shortChar, explanation) in conflicting)
        {
            arity.Remove(shortChar);

            // Trace, not throw — see the remarks above. This is the only channel that can carry a
            // framework concern without failing a legal composition.
            Trace.TraceWarning(explanation);
        }

        return (new CliShortOptionSchema(arity, conflicting.Keys), registered);

        static string Describe(CliShortOptionArity value) => value switch
        {
            CliShortOptionArity.Flag => "a flag (takes no value)",
            CliShortOptionArity.Map => "a map (takes a [key] and a value)",
            _ => "a scalar (takes one value)",
        };
    }

    // ---------------------------------------------------------------------------------------
    //  Builder (the ICliApplicationBuilder implementation)
    // ---------------------------------------------------------------------------------------

    private sealed record Service(
        Type ServiceType,
        Func<object?> InstanceFactory,
        Func<ValueTask>? Release,
        ImmutableArray<CliRouteAttribute> RootRoutes);

    private sealed class Builder : ICliApplicationBuilder, ICliCommandLifetimeBuilder
    {
        public List<CliMiddleware> Middleware { get; } = new();
        public List<Service> Services { get; } = new();
        public ICliConsole? Console { get; set; }
        public Func<string>? VersionFactory { get; set; }
        public IReadOnlyList<string>? VersionTriggers { get; set; }
        public IReadOnlyList<string>? HelpTriggers { get; set; }
        public bool HelpSuppressed { get; set; }

        public ICliApplicationBuilder WithConsole(ICliConsole console)
        {
            Console = ThrowIf.ArgumentNull(console);
            return this;
        }

        public ICliApplicationBuilder WithVersion(string version)
        {
            ThrowIf.ArgumentNull(version);
            VersionFactory = () => version;
            return this;
        }

        public ICliApplicationBuilder WithVersion(Func<string> versionFactory)
        {
            ThrowIf.ArgumentNull(versionFactory);
            VersionFactory = versionFactory;
            return this;
        }

        public ICliApplicationBuilder WithVersion(Action<CliVersionBuilder> configure)
        {
            ThrowIf.ArgumentNull(configure);
            var b = new CliVersionBuilder();
            configure(b);
            VersionFactory = b.TextFactory ?? DefaultVersionFactory;
            VersionTriggers = b.CustomTriggers;
            return this;
        }

        public ICliApplicationBuilder WithHelp(Action<CliHelpBuilder> configure)
        {
            ThrowIf.ArgumentNull(configure);
            var b = new CliHelpBuilder();
            configure(b);
            HelpTriggers = b.CustomTriggers;
            HelpSuppressed = false;
            return this;
        }

        public ICliApplicationBuilder SuppressHelp()
        {
            HelpSuppressed = true;
            return this;
        }

        public ICliApplicationBuilder UseMiddleware(CliMiddleware middleware)
        {
            ThrowIf.ArgumentNull(middleware);
            Middleware.Add(middleware);
            return this;
        }

        public ICliApplicationBuilder AddCommands(object instance, IEnumerable<CliRouteAttribute> rootRoutes)
        {
            ThrowIf.ArgumentNull(instance);
            Services.Add(new Service(instance.GetType(), () => instance, null, [..rootRoutes.Distinct()]));
            return this;
        }

        public ICliApplicationBuilder AddCommands(Type serviceType, Func<object> factory, IEnumerable<CliRouteAttribute> rootRoutes)
        {
            ThrowIf.ArgumentNull(factory);
            Services.Add(new Service(serviceType, () => factory(), null, [..rootRoutes.Distinct()]));
            return this;
        }

        ICliApplicationBuilder ICliCommandLifetimeBuilder.AddCommands(
            Type serviceType,
            Func<object> factory,
            Func<ValueTask> release,
            IEnumerable<CliRouteAttribute> rootRoutes)
        {
            ThrowIf.ArgumentNull(serviceType);
            ThrowIf.ArgumentNull(factory);
            ThrowIf.ArgumentNull(release);
            ThrowIf.ArgumentNull(rootRoutes);
            Services.Add(new Service(serviceType, () => factory(), release, [..rootRoutes.Distinct()]));
            return this;
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Per-action wrapper
    // ---------------------------------------------------------------------------------------

    private sealed class CliAction(
        CliMethodInfo method,
        Func<object?> instanceFactory,
        Func<ValueTask>? release,
        ICliConsole console)
    {
        [DebuggerStepThrough]
        public bool IsMatch(CliInvocation invocation) => method.IsMatch(invocation);

        [DebuggerStepThrough]
        public async Task<int> InvokeAsync(CliInvocation invocation, CancellationToken cancellationToken)
        {
            try
            {
                return await method
                    .InvokeAsync(instanceFactory(), invocation, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (release is not null)
                {
                    await release().ConfigureAwait(false);
                }
            }
        }

        public void ShowHelp(CliInvocation invocation) =>
            console.Out.WriteLine(method.ToCommandHelpString(invocation.ExecutableName));

        [DebuggerStepThrough]
        public double RankByOptions(CliInvocation invocation) => method.RankByOptions(invocation);

        public IReadOnlyList<string> LiteralPrefix => method.LiteralPrefix;

        public override string ToString() => method.RouteSignature;

        public string RouteSignature => method.RouteSignature;

        public string MethodDescription => method.ToString();

        public Reflection.CliRouteModel RouteModel => method.RouteModel;

        public IEnumerable<ICliOptionMemberInfo> GetOptionInfos() => method.GetOptions();

        /// <summary>
        /// True when this route declares <paramref name="token"/> (a dashed option form such as
        /// <c>-h</c> or <c>--host</c>) as one of its own option aliases. Short options are compared
        /// case-sensitively (preserving the <c>-V</c>/<c>-v</c> convention); longer forms are
        /// case-insensitive — the same rule the trigger matcher uses.
        /// </summary>
        public bool DeclaresOptionAlias(string token)
        {
            foreach (var option in method.GetOptions())
            {
                foreach (var alias in option.Aliases)
                {
                    if (CliAliasComparer.Instance.Equals(alias, token)) return true;
                }
            }
            return false;
        }
    }
}


using Portico.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Portico;

/// <summary>
/// Fluent builder for <see cref="CliApplication"/>. Intentionally small: the framework owns
/// routing, binding, dispatch, and protocol-level flags (<c>--version</c>, <c>--help</c>);
/// everything else is the user's concern. Presentation (logos, descriptions, colors) and DI
/// wiring are deliberately not exposed here — handle those in your own code.
/// </summary>
public interface ICliApplicationBuilder
{
    /// <summary>
    /// Overrides the streams the framework writes help and errors to. Default is
    /// <see cref="SystemCliConsole.Instance"/>; inject a test-only console to capture output.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .WithConsole(testConsole)
    ///     .AddCommands(new MyTool()));
    /// </code></example>
    ICliApplicationBuilder WithConsole(ICliConsole console);

    /// <summary>
    /// Configures the string printed when the user passes <c>--version</c> or <c>-V</c>. The
    /// version check runs before command matching and takes precedence over any registered route,
    /// so <c>myapp --version</c> prints the version even if a route happens to also match.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .WithVersion("myapp 1.4.0")
    ///     .AddCommands(new MyTool()));
    /// </code></example>
    ICliApplicationBuilder WithVersion(string version);

    /// <summary>
    /// Lazy overload of <see cref="WithVersion(string)"/> — the factory is invoked on every
    /// version request, making it suitable for tooling that reads the version from disk or an
    /// assembly-scanning helper.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .WithVersion(() =&gt; File.ReadAllText("VERSION").Trim())
    ///     .AddCommands(new MyTool()));
    /// </code></example>
    ICliApplicationBuilder WithVersion(Func<string> versionFactory);

    /// <summary>
    /// Configures the <c>--version</c> response using a builder — allows setting the printed
    /// text (<see cref="CliVersionBuilder.Text(string)"/>) and replacing the default triggers
    /// (<see cref="CliVersionBuilder.Triggers"/>). Use this overload when you need a subcommand
    /// form (<c>myapp version</c>) alongside or instead of <c>--version</c>. Omitting
    /// <c>Text(...)</c> falls back to the <see cref="AssemblyInformationalVersionAttribute"/>
    /// auto-discovery that <see cref="WithVersion()"/> performs.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .WithVersion(v =&gt; v.Text("myapp 1.4.0").Triggers("--version", "version"))
    ///     .AddCommands(new MyTool()));
    /// </code></example>
    ICliApplicationBuilder WithVersion(Action<CliVersionBuilder> configure);

    /// <summary>
    /// Customizes help detection. By default the framework responds to <c>--help</c>,
    /// <c>help</c>, <c>-h</c>, <c>-?</c>, and <c>?</c>; use <see cref="CliHelpBuilder.Triggers"/>
    /// to replace that set (e.g. add <c>/?</c> for Windows-idiomatic users, or add a <c>man</c>
    /// verb). Calling this method does not change help rendering — it only changes detection.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .WithHelp(h =&gt; h.Triggers("--help", "-h", "/?"))
    ///     .AddCommands(new MyTool()));
    /// </code></example>
    ICliApplicationBuilder WithHelp(Action<CliHelpBuilder> configure);

    /// <summary>
    /// Disables help detection entirely. The framework will no longer intercept
    /// <c>--help</c>/<c>-h</c>/etc.; those tokens will either bind to user-defined options
    /// (if any) or raise a usage error. Use for daemon / headless CLIs whose routes are not
    /// meant to be discovered interactively.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .SuppressHelp()
    ///     .AddCommands(new DaemonTool()));
    /// </code></example>
    ICliApplicationBuilder SuppressHelp();

    /// <summary>
    /// Registers a <see cref="CliMiddleware"/> — cross-cutting options (e.g. <c>--trace-level</c>)
    /// combined with lifecycle hooks (<see cref="CliMiddleware.OnExecutingAction"/>,
    /// <see cref="CliMiddleware.OnActionExecuted"/>, <see cref="CliMiddleware.OnError"/>) that
    /// wrap every registered command. Multiple registrations run in declared order — the CLI
    /// equivalent of <c>app.UseMiddleware&lt;T&gt;()</c> in ASP.NET Core.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .UseMiddleware(traceMiddleware)
    ///     .AddCommands(new MyTool()));
    /// </code></example>
    ICliApplicationBuilder UseMiddleware(CliMiddleware middleware);

    /// <summary>
    /// Registers an object whose <see cref="CliRouteAttribute"/>-decorated methods are the CLI's
    /// commands. <paramref name="rootRoutes"/> prefixes every route on the instance — e.g. an
    /// instance with <c>[CliRoute("migrate")]</c> plus a root route of <c>db</c> matches
    /// <c>myapp db migrate</c>. A route prefix declared via <c>[CliRoute]</c> on the class or
    /// interface composes on top of <paramref name="rootRoutes"/>.
    /// <para>
    /// A mount prefix is <b>literal segments only</b>. It is applied to commands declared elsewhere,
    /// which have no parameter for a <c>{placeholder}</c> to bind to, so one is refused at
    /// <see cref="CliApplication.Create"/> rather than compiled into a literal nobody could type.
    /// A type-level <c>[CliRoute]</c> is the opposite — it decorates the same author's methods, so
    /// its placeholders bind exactly as method-level ones do.
    /// </para>
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .AddCommands(new MigrationTool(), new[] { new CliRouteAttribute("db") }));
    /// </code></example>
    ICliApplicationBuilder AddCommands(object instance, IEnumerable<CliRouteAttribute> rootRoutes);

    /// <summary>
    /// Registers a command-handler type whose instance is produced by <paramref name="factory"/>
    /// on every invocation. Lifetime is the factory's concern — return the same instance for
    /// singleton semantics, or build afresh for per-invocation scopes. Bridge any DI container
    /// (MEDI, Autofac, Unity, plain lambdas) by forwarding to its resolve method.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .AddCommands(typeof(MyTool), () =&gt; serviceProvider.GetRequiredService&lt;MyTool&gt;(), []));
    /// </code></example>
    ICliApplicationBuilder AddCommands(Type serviceType, Func<object> factory, IEnumerable<CliRouteAttribute> rootRoutes);

    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg.AddCommands(new MyTool()));
    /// </code></example>
    [DebuggerStepThrough]
    public sealed ICliApplicationBuilder AddCommands(object instance) => AddCommands(instance, []);

    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg.AddCommands(() =&gt; new MyTool()));
    /// </code></example>
    [DebuggerStepThrough]
    public sealed ICliApplicationBuilder AddCommands<T>(Func<T> factory) where T : class =>
        AddCommands(typeof(T), () => factory(), []);

    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .AddCommands(() =&gt; new MigrationTool(), new[] { new CliRouteAttribute("db") }));
    /// </code></example>
    [DebuggerStepThrough]
    public sealed ICliApplicationBuilder AddCommands<T>(Func<T> factory, IEnumerable<CliRouteAttribute> rootRoutes) where T : class =>
        AddCommands(typeof(T), () => factory(), rootRoutes);

    /// <summary>
    /// BCL-native convenience: render a <see cref="System.Version"/> directly. Equivalent to
    /// <c>WithVersion(version.ToString())</c> but avoids the string stringify at the call site.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .WithVersion(new Version(1, 4, 0))
    ///     .AddCommands(new MyTool()));
    /// </code></example>
    [DebuggerStepThrough]
    public sealed ICliApplicationBuilder WithVersion(Version version)
    {
        ThrowIf.ArgumentNull(version);
        return WithVersion(version.ToString());
    }

    /// <summary>
    /// Auto-discovers the entry assembly's version — preferring
    /// <see cref="AssemblyInformationalVersionAttribute"/> (lets you encode build metadata via
    /// <c>&lt;Version&gt;</c> / <c>&lt;InformationalVersion&gt;</c> in your csproj) and falling
    /// back to <see cref="AssemblyName.Version"/>. The printed form is
    /// <c>"{assembly-name} {version}"</c>.
    /// </summary>
    /// <remarks>
    /// This is the <em>.NET-native</em> version of <see cref="WithVersion(string)"/>: declare
    /// your version once in the csproj, then write <c>.WithVersion()</c> — no duplication
    /// between the build metadata and the CLI output. Matches how <c>cargo</c> / <c>npm</c>
    /// package managers inject their versions from manifest files.
    /// </remarks>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .WithVersion() // reads AssemblyInformationalVersionAttribute from the entry assembly
    ///     .AddCommands(new MyTool()));
    /// </code></example>
    [DebuggerStepThrough]
    public sealed ICliApplicationBuilder WithVersion() => WithVersion(CliVersionDiscovery.FromEntryAssembly);
}

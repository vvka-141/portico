using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Portico.DependencyInjection;

namespace Portico.Hosting;

/// <summary>
/// Runs a Portico CLI on the Generic Host — the shape a backend service already has.
/// </summary>
/// <remarks>
/// <para>
/// The service's <c>HostApplicationBuilder</c> already carries its configuration, its logging and its
/// container. Its admin CLI should reuse all of it:
/// </para>
/// <code>
/// var builder = Host.CreateApplicationBuilder(args);
/// builder.Services.AddSingleton&lt;IMigrator, Migrator&gt;();
/// builder.Services.AddPorticoCommands&lt;IAdminTool, AdminTool&gt;();
///
/// return await builder.Build().RunPorticoAsync(args);
/// </code>
/// <para>
/// <b>The CLI is not a hosted service.</b> A CLI is one command, one invocation, one exit code
/// (CHARTER §5). Modelling it as an <see cref="IHostedService"/> would background the very thing the
/// user is waiting for, and lose its exit code — so <see cref="RunPorticoAsync"/> returns
/// <see cref="Task{Int32}"/> and that value is what <c>Main</c> returns.
/// </para>
/// </remarks>
public static class CliHostExtensions
{
    private const string TrimWarning =
        "Portico discovers routes and binds options via reflection. " +
        "A trimmed or NativeAOT publish will fail at runtime. " +
        "See docs/explanation/aot.md in the Portico repository.";
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> as the scoped implementation of the
    /// <typeparamref name="TContract"/> command contract, and records the contract so
    /// <see cref="RunPorticoAsync"/> can mount it.
    /// </summary>
    /// <typeparam name="TContract">The interface carrying the <c>[CliRoute]</c> methods.</typeparam>
    /// <typeparam name="TImplementation">The class that implements it.</typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <param name="rootRoutes">
    /// Optional route prefix to mount the contract under, for a CLI composed of several contracts.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <example><code>
    /// builder.Services.AddPorticoCommands&lt;IAdminTool, AdminTool&gt;();
    /// builder.Services.AddPorticoCommands&lt;IQueueTool, QueueTool&gt;("queue");   // mounted
    /// </code></example>
    public static IServiceCollection AddPorticoCommands<TContract, TImplementation>(
        this IServiceCollection services,
        params string[] rootRoutes)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TContract, TImplementation>();
        return services.AddPorticoCommands<TContract>(rootRoutes);
    }

    /// <summary>
    /// Records <typeparamref name="TContract"/> as a command contract, for the case where you have
    /// already registered its implementation yourself (a factory, a decorator, a different lifetime).
    /// </summary>
    /// <typeparam name="TContract">The interface carrying the <c>[CliRoute]</c> methods.</typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <param name="rootRoutes">Optional route prefix to mount the contract under.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <example><code>
    /// builder.Services.AddScoped&lt;IAdminTool&gt;(sp =&gt; new AdminTool(sp.GetRequiredService&lt;IMigrator&gt;()));
    /// builder.Services.AddPorticoCommands&lt;IAdminTool&gt;();
    /// </code></example>
    public static IServiceCollection AddPorticoCommands<TContract>(
        this IServiceCollection services,
        params string[] rootRoutes)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootRoutes);

        // The host's console lifetime logs "Application started. Press Ctrl+C to shut down." — true
        // for a service, noise for a CLI whose whole output is the command's. Suppressed by default;
        // a user who wants it back can re-Configure the option after this call.
        services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

        services.AddSingleton(new CliCommandRegistration(typeof(TContract), rootRoutes));
        return services;
    }

    /// <summary>
    /// Runs the CLI against the host's services and returns the command's exit code — the value your
    /// <c>Main</c> should return.
    /// </summary>
    /// <param name="host">The built host.</param>
    /// <param name="args">The argv from <c>Main</c>, without the executable name.</param>
    /// <param name="configure">
    /// Optional extra application configuration — middleware, version, help triggers. The contracts
    /// registered with <c>AddPorticoCommands</c> are already added when this runs.
    /// </param>
    /// <returns>The dispatched command's exit code.</returns>
    /// <exception cref="InvalidOperationException">No contract was registered with <c>AddPorticoCommands</c>.</exception>
    /// <remarks>
    /// <para>
    /// <b>Graceful shutdown is the host's, not ours.</b> The host is started, so its
    /// <see cref="IHostApplicationLifetime"/> owns Ctrl+C and SIGTERM; its
    /// <see cref="IHostApplicationLifetime.ApplicationStopping"/> token is what the CLI runs under.
    /// Because that token <see cref="CancellationToken.CanBeCanceled"/>, the core deliberately skips
    /// its own SIGINT/SIGTERM wiring — one mechanism, not two racing handlers. A cancelled command
    /// still exits 130.
    /// </para>
    /// <para>
    /// The host is started and stopped around the dispatch, so any <see cref="IHostedService"/> the
    /// service registers runs for the duration of the command. If your host registers long-running
    /// workers you do not want a CLI invocation to start, register them in the service's own entry
    /// point rather than in the shared builder.
    /// </para>
    /// <para>
    /// <b>One host, one command.</b> The host is stopped when the command finishes, and a stopped
    /// host is not restartable — its <see cref="IHostApplicationLifetime.ApplicationStopping"/> token
    /// stays cancelled. A second <see cref="RunPorticoAsync"/> on the same host therefore cancels
    /// immediately rather than half-running. That is the right shape for a CLI process; build a fresh
    /// host if you genuinely need to dispatch twice (a test, usually).
    /// </para>
    /// </remarks>
    /// <example><code>
    /// public static async Task&lt;int&gt; Main(string[] args)
    /// {
    ///     var builder = Host.CreateApplicationBuilder(args);
    ///     builder.Services.AddPorticoCommands&lt;IAdminTool, AdminTool&gt;();
    ///     return await builder.Build().RunPorticoAsync(args);
    /// }
    /// </code></example>
    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    public static async Task<int> RunPorticoAsync(
        this IHost host,
        string[] args,
        Action<ICliApplicationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(args);

        var registrations = host.Services.GetServices<CliCommandRegistration>().ToList();
        if (registrations.Count == 0)
        {
            throw new InvalidOperationException(
                "No CLI command contracts are registered. Call " +
                "services.AddPorticoCommands<TContract, TImplementation>() on the host builder before " +
                "RunPorticoAsync — the CLI is assembled from the container, so a contract the container " +
                "does not know about has no routes to dispatch.");
        }

        var app = CliApplication.Create(cfg =>
        {
            foreach (var registration in registrations)
            {
                cfg.AddCommands(registration.ContractType, host.Services, registration.RootRoutes);
            }
            configure?.Invoke(cfg);
        });

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        try
        {
            // StartAsync is INSIDE the try: if an IHostedService faults during startup (the scenario
            // the <remarks> above anticipates), StopAsync must still run so any service that already
            // started is shut down and disposed rather than leaked. Host.StopAsync tolerates a
            // partially-started host. POR-62.
            await host.StartAsync().ConfigureAwait(false);
            return await app.RunAsync(args, lifetime.ApplicationStopping).ConfigureAwait(false);
        }
        finally
        {
            // StopAsync, not the stopping token: the command is over either way, and hosted services
            // registered by the service need their normal shutdown.
            await host.StopAsync().ConfigureAwait(false);
        }
    }
}

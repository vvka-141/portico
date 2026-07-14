using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Portico.DependencyInjection;

/// <summary>
/// Resolves Portico command contracts and middleware from an <see cref="IServiceProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole adapter. The core <c>Portico</c> package has no dependencies and does not know
/// what a container is; it already takes a factory
/// (<c>AddCommands&lt;T&gt;(Func&lt;T&gt;)</c>), and these extensions supply one that resolves from
/// your provider. Nothing in the core changed to make this work.
/// </para>
/// <para>
/// <b>The factory stays lazy, which is the point.</b> Registering a <c>migrate</c> command that opens
/// a connection pool and a <c>health</c> command that does not means <c>myapp health</c> never touches
/// the database: nothing is constructed at <c>CliApplication.Create</c>, nothing is constructed for
/// <c>--help</c> or an unknown command, and a dispatched route constructs only the command it reached.
/// </para>
/// </remarks>
public static class CliApplicationBuilderExtensions
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as a command contract resolved from <paramref name="services"/>,
    /// in a fresh <see cref="IServiceScope"/> opened for the dispatched command and disposed when it
    /// completes — success, failure, or cancellation.
    /// </summary>
    /// <typeparam name="T">The command contract. Its <c>[CliRoute]</c> methods define the CLI.</typeparam>
    /// <param name="builder">The application builder.</param>
    /// <param name="services">The provider to resolve from. Its root scope is never resolved against.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <example><code>
    /// var services = new ServiceCollection()
    ///     .AddScoped&lt;IAdminTool, AdminTool&gt;()
    ///     .AddScoped&lt;IDbConnection&gt;(_ =&gt; new NpgsqlConnection(connectionString))
    ///     .BuildServiceProvider();
    ///
    /// return CliApplication
    ///     .Create(cfg =&gt; cfg.AddCommands&lt;IAdminTool&gt;(services))
    ///     .Run(args);
    /// </code></example>
    public static ICliApplicationBuilder AddCommands<T>(
        this ICliApplicationBuilder builder,
        IServiceProvider services) where T : class =>
        builder.AddCommands<T>(services, []);

    /// <summary>
    /// Registers <typeparamref name="T"/> from <paramref name="services"/>, mounted under
    /// <paramref name="rootRoutes"/> — the composition form, for a master CLI over several
    /// independently-built contracts.
    /// </summary>
    /// <typeparam name="T">The command contract.</typeparam>
    /// <param name="builder">The application builder.</param>
    /// <param name="services">The provider to resolve from.</param>
    /// <param name="rootRoutes">The route prefix this contract is mounted under.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .AddCommands&lt;IStorageTool&gt;(services, [new CliRouteAttribute("storage")])
    ///     .AddCommands&lt;IQueueTool&gt;(services, [new CliRouteAttribute("queue")]));
    /// </code></example>
    public static ICliApplicationBuilder AddCommands<T>(
        this ICliApplicationBuilder builder,
        IServiceProvider services,
        IEnumerable<CliRouteAttribute> rootRoutes) where T : class =>
        builder.AddCommands(typeof(T), services, rootRoutes);

    /// <summary>
    /// The non-generic form of <see cref="AddCommands{T}(ICliApplicationBuilder, IServiceProvider,
    /// IEnumerable{CliRouteAttribute})"/>, for callers that discover contract types at run time —
    /// <c>Portico.Hosting</c> registers the contracts collected from the service collection this way.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="contractType">The command contract's type, as registered in the container.</param>
    /// <param name="services">The provider to resolve from.</param>
    /// <param name="rootRoutes">The route prefix this contract is mounted under; empty for none.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <example><code>
    /// cfg.AddCommands(typeof(IAdminTool), serviceProvider, []);
    /// </code></example>
    public static ICliApplicationBuilder AddCommands(
        this ICliApplicationBuilder builder,
        Type contractType,
        IServiceProvider services,
        IEnumerable<CliRouteAttribute> rootRoutes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(contractType);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(rootRoutes);

        // The middleware closes the scope the factory opens. Registering it here — rather than asking
        // the user to — is what keeps `AddScoped` from silently behaving like `AddSingleton`.
        // Registering it twice is harmless: closing an already-closed scope is a no-op.
        builder.UseMiddleware(new CliServiceScopeMiddleware());

        return builder.AddCommands(contractType, () => CliDispatchScope.Resolve(contractType, services), rootRoutes);
    }

    /// <summary>
    /// Registers a <see cref="CliMiddleware"/> resolved from <paramref name="services"/>, so
    /// cross-cutting CLI behaviour can take constructor dependencies like anything else you write.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware type, registered in the container.</typeparam>
    /// <param name="builder">The application builder.</param>
    /// <param name="services">The provider to resolve from.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Middleware is application-scoped, not per-dispatch: it is resolved once, here, and the
    /// framework clones it per invocation to bind that invocation's options. Inject stateless
    /// dependencies into it — the clone is shallow, so a reference-typed field is shared across
    /// concurrent dispatches rather than duplicated.
    /// </remarks>
    /// <example><code>
    /// var services = new ServiceCollection()
    ///     .AddSingleton&lt;IAuditLog, AuditLog&gt;()
    ///     .AddSingleton&lt;AuditMiddleware&gt;()      // ctor takes IAuditLog
    ///     .BuildServiceProvider();
    ///
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .AddCommands&lt;IAdminTool&gt;(services)
    ///     .UseMiddleware&lt;AuditMiddleware&gt;(services));
    /// </code></example>
    public static ICliApplicationBuilder UseMiddleware<TMiddleware>(
        this ICliApplicationBuilder builder,
        IServiceProvider services) where TMiddleware : CliMiddleware
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(services);

        return builder.UseMiddleware(services.GetRequiredService<TMiddleware>());
    }
}

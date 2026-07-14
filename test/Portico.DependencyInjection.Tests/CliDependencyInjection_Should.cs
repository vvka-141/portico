using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Portico.Testing;
using Xunit;

namespace Portico.DependencyInjection.Tests;

// ReSharper disable once InconsistentNaming
//
// POR-32. The adapter's whole job: resolve command contracts from a container without the core ever
// learning what a container is. These tests pin the two properties that make it worth shipping —
// the factory stays lazy, and a scoped service is actually scoped to the dispatched command.
public sealed class CliDependencyInjection_Should
{
    // --- Contracts under test ---------------------------------------------------------------

    public interface IAdminTool
    {
        [CliRoute("db migrate")]
        [CliCommandExample("db migrate")]
        int Migrate();
    }

    public interface IHealthTool
    {
        [CliRoute("health")]
        [CliCommandExample("health")]
        int Health();
    }

    /// <summary>Records what the container built, and what got disposed.</summary>
    public sealed class Ledger
    {
        public List<string> Constructed { get; } = [];
        public List<string> Disposed { get; } = [];
    }

    public sealed class Database(Ledger ledger) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            ledger.Disposed.Add(nameof(Database));
        }
    }

    public sealed class AdminTool : IAdminTool
    {
        private readonly Database _database;
        private readonly Ledger _ledger;

        public AdminTool(Database database, Ledger ledger)
        {
            _database = database;
            _ledger = ledger;
            ledger.Constructed.Add(nameof(AdminTool));
        }

        public int Migrate()
        {
            // The injected service must be alive while the command runs — the scope closes after.
            Assert.False(_database.IsDisposed);
            _ledger.Constructed.Add("AdminTool.Migrate");
            return 0;
        }
    }

    public sealed class HealthTool(Ledger ledger) : IHealthTool
    {
        public int Health()
        {
            ledger.Constructed.Add("HealthTool.Health");
            return 0;
        }
    }

    public sealed class ExpensiveTool : IHealthTool
    {
        public ExpensiveTool(Ledger ledger) => ledger.Constructed.Add(nameof(ExpensiveTool));

        public int Health() => 0;
    }

    private static (ServiceProvider Services, Ledger Ledger) Container()
    {
        var ledger = new Ledger();
        var services = new ServiceCollection()
            .AddSingleton(ledger)
            .AddScoped<Database>()
            .AddScoped<IAdminTool, AdminTool>()
            .AddScoped<IHealthTool, HealthTool>()
            .BuildServiceProvider();

        return (services, ledger);
    }

    private static CliTestHarness Harness(IServiceProvider services) =>
        CliTestHarness.ForApplication(cfg => cfg
            .AddCommands<IAdminTool>(services)
            .AddCommands<IHealthTool>(services));

    // --- The tests ---------------------------------------------------------------------------

    [Fact]
    public void Dispatch_A_Command_Resolved_From_The_Container()
    {
        var (services, ledger) = Container();

        Harness(services).Run("admin db migrate").ExpectExit(0);

        Assert.Contains("AdminTool.Migrate", ledger.Constructed);
    }

    [Fact]
    public void Construct_Only_The_Command_That_Was_Invoked()
    {
        // The lazy seam is the reason DI is worth having here: `myapp health` must not open the
        // database pool that `db migrate` needs.
        var (services, ledger) = Container();

        Harness(services).Run("admin health").ExpectExit(0);

        Assert.Contains("HealthTool.Health", ledger.Constructed);
        Assert.DoesNotContain(nameof(AdminTool), ledger.Constructed);
        Assert.DoesNotContain(nameof(Database), ledger.Disposed);   // never built, so never disposed
    }

    [Fact]
    public void Construct_Nothing_At_All_For_Create_Help_Or_An_Unknown_Command()
    {
        var (services, ledger) = Container();
        var harness = Harness(services);   // CliApplication.Create has already run here

        Assert.Empty(ledger.Constructed);

        harness.Run("admin --help").ExpectExit(0);
        harness.Run("admin db migrate --help").ExpectExit(0);
        harness.Run("admin nonsense").ExpectExit(2);

        Assert.Empty(ledger.Constructed);
    }

    [Fact]
    public void Dispose_The_Dispatch_Scope_When_The_Command_Completes()
    {
        // The scope decision (POR-32): one IServiceScope per dispatched command, disposed at the end
        // of it. Resolving AddScoped services from the root provider — the alternative — is the
        // classic ASP.NET footgun: nothing is ever disposed and every "scoped" service is a singleton.
        var (services, ledger) = Container();

        Harness(services).Run("admin db migrate").ExpectExit(0);

        Assert.Equal([nameof(Database)], ledger.Disposed);
    }

    [Fact]
    public void Dispose_The_Dispatch_Scope_When_The_Command_Throws()
    {
        // Disposal is symmetric or it is not disposal. OnActionExecuted runs from the invoker's
        // finally, which is exactly why the scope is closed there and not on the happy path.
        var ledger = new Ledger();
        var services = new ServiceCollection()
            .AddSingleton(ledger)
            .AddScoped<Database>()
            .AddScoped<IAdminTool, ThrowingTool>()
            .BuildServiceProvider();

        var harness = CliTestHarness.ForApplication(cfg => cfg.AddCommands<IAdminTool>(services));

        harness.Run("admin db migrate").ExpectExit(1);

        Assert.Equal([nameof(Database)], ledger.Disposed);
    }

    public sealed class ThrowingTool(Database database, Ledger ledger) : IAdminTool
    {
        public int Migrate()
        {
            _ = database;
            _ = ledger;
            throw new InvalidOperationException("migration failed");
        }
    }

    [Fact]
    public void Give_Each_Dispatch_Its_Own_Scope()
    {
        // Two commands in one process (a test harness, or a REPL-ish host) must not share services.
        var (services, ledger) = Container();
        var harness = Harness(services);

        harness.Run("admin db migrate").ExpectExit(0);
        harness.Run("admin db migrate").ExpectExit(0);

        Assert.Equal([nameof(Database), nameof(Database)], ledger.Disposed);
    }

    [Fact]
    public void Share_One_Scope_Across_The_Services_Of_A_Single_Dispatch()
    {
        // The command and its dependencies come from the same scope — otherwise "scoped" would mean
        // nothing. AdminTool and Database are both AddScoped; Migrate() asserts the Database it was
        // handed is still alive, and the ledger proves exactly one was built and disposed.
        var (services, ledger) = Container();

        Harness(services).Run("admin db migrate").ExpectExit(0);

        Assert.Single(ledger.Disposed);
    }

    // --- Middleware from the container -------------------------------------------------------

    public sealed class AuditMiddleware(Ledger ledger) : CliMiddleware
    {
        public override void OnExecutingAction(CliInvocation invocation)
        {
            ledger.Constructed.Add($"audit:{invocation.Segments[0]}");
            base.OnExecutingAction(invocation);
        }
    }

    [Fact]
    public void Resolve_Middleware_From_The_Container()
    {
        // POR-26 unblocked this: middleware is user-constructed and cloned, never Activator'd, so a
        // constructor dependency is legitimate — which is exactly how a container injects one.
        var (services, ledger) = Container();
        var withMiddleware = new ServiceCollection()
            .AddSingleton(ledger)
            .AddScoped<Database>()
            .AddScoped<IAdminTool, AdminTool>()
            .AddSingleton<AuditMiddleware>()
            .BuildServiceProvider();

        var harness = CliTestHarness.ForApplication(cfg => cfg
            .AddCommands<IAdminTool>(withMiddleware)
            .UseMiddleware<AuditMiddleware>(withMiddleware));

        harness.Run("admin db migrate").ExpectExit(0);

        Assert.Contains("audit:db", ledger.Constructed);
    }

    // --- Composition -------------------------------------------------------------------------

    [Fact]
    public void Mount_A_Container_Resolved_Contract_Under_A_Root_Route()
    {
        var (services, ledger) = Container();

        var harness = CliTestHarness.ForApplication(cfg => cfg
            .AddCommands<IAdminTool>(services, [new CliRouteAttribute("ops")]));

        harness.Run("admin ops db migrate").ExpectExit(0);
        harness.Run("admin db migrate").ExpectExit(2);   // a mount is a move, not an alias

        Assert.Contains("AdminTool.Migrate", ledger.Constructed);
    }

    // --- Guard rails --------------------------------------------------------------------------

    [Fact]
    public void Reject_A_Null_Provider()
    {
        // The cast is load-bearing: a bare `null!` binds to the core's AddCommands<T>(Func<T>)
        // instead, because an interface's own method beats an extension method.
        Assert.Throws<ArgumentNullException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands<IAdminTool>((IServiceProvider)null!)));
    }

    [Fact]
    public void Fail_Loudly_When_The_Contract_Is_Not_Registered()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var result = CliTestHarness
            .ForApplication(cfg => cfg.AddCommands<IAdminTool>(services))
            .Run("admin db migrate");

        // The framework turns any unhandled exception into exit 1 plus a message on stderr, so
        // GetRequiredService's complaint reaches the user verbatim — the adapter neither swallows
        // it nor reinterprets it. A container misconfiguration must not look like a routing failure.
        result.ExpectExit(1).ExpectError(nameof(IAdminTool));
    }
}

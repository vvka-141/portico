using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Portico.Hosting.Tests;

// ReSharper disable once InconsistentNaming
//
// POR-33. The two things this adapter has to get right, and the only two that are hard:
//   1. the command's int exit code must reach Main — IHost.RunAsync() returns void, and a CLI that
//      cannot report failure to a shell is not a CLI;
//   2. graceful shutdown must be ONE mechanism. The host owns Ctrl+C/SIGTERM; the core skips its own
//      wiring precisely because the token it is handed CanBeCanceled. Two handlers racing on Ctrl+C
//      is the failure mode.
public sealed class CliHost_Should
{
    public interface IAdminTool
    {
        [CliRoute("db migrate")]
        [CliCommandExample("db migrate")]
        int Migrate();

        [CliRoute("fail")]
        [CliCommandExample("fail")]
        int Fail();

        [CliRoute("wait")]
        [CliCommandExample("wait")]
        Task<int> WaitAsync(CancellationToken cancellation);
    }

    /// <summary>A service the host — not Portico — registered. Injecting it is the whole point.</summary>
    public sealed class Migrator
    {
        public List<string> Applied { get; } = [];
    }

    public sealed class AdminTool(Migrator migrator, CliHost_Should.Gate? gate = null) : IAdminTool
    {
        public int Migrate()
        {
            migrator.Applied.Add("003_add_index");
            return 0;
        }

        public int Fail() => throw new CliExitException("nope") { ExitCode = 17 };

        public async Task<int> WaitAsync(CancellationToken cancellation)
        {
            gate?.Started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellation);
            return 0;
        }
    }

    /// <summary>Lets a test wait until the handler is actually running before it cancels.</summary>
    public sealed class Gate
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static IHostBuilder Builder(Action<IServiceCollection>? configure = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<Migrator>();
                services.AddPorticoCommands<IAdminTool, AdminTool>();
                configure?.Invoke(services);
            });

    [Fact]
    public async Task Inject_A_Service_The_Host_Registered()
    {
        using var host = Builder().Build();

        var exitCode = await host.RunPorticoAsync(["db", "migrate"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(["003_add_index"], host.Services.GetRequiredService<Migrator>().Applied);
    }

    [Fact]
    public async Task Return_The_Commands_Exit_Code_To_Main()
    {
        // IHost.RunAsync() returns void. If the exit code did not survive the host, a failing
        // command would exit 0 and CI would go green on a broken migration.
        using var host = Builder().Build();

        Assert.Equal(17, await host.RunPorticoAsync(["fail"]));
    }

    [Fact]
    public async Task Exit_130_When_The_Hosts_Lifetime_Stops_The_Application()
    {
        var gate = new Gate();
        using var host = Builder(services => services.AddSingleton(gate)).Build();

        var run = host.RunPorticoAsync(["wait"]);

        // Cancel the way the host does it — StopApplication() is exactly what its console lifetime
        // calls on Ctrl+C/SIGTERM. The handler's CancellationToken is the host's ApplicationStopping.
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        Assert.Equal(CliExitException.CancelledExitCode, await run.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Hand_The_Handler_A_Cancellable_Token_So_The_Core_Installs_No_Second_Handler()
    {
        // This is what "one mechanism, not two" reduces to in code: the core's
        // RunWithAutoCancelAsync installs Console.CancelKeyPress + SIGTERM handlers ONLY when the
        // token it is given cannot be cancelled. Handing it the host's ApplicationStopping token —
        // which always CanBeCanceled — is what makes it stand down.
        CancellationToken observed = default;

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped<ICaptureTool>(_ => new TokenCapturingTool(t => observed = t));
                services.AddPorticoCommands<ICaptureTool>();
            })
            .Build();

        Assert.Equal(0, await host.RunPorticoAsync(["capture"]));

        Assert.True(observed.CanBeCanceled);

        // ...and it is the host's token, not one Portico minted: it was already cancelled by the
        // StopAsync that RunPorticoAsync runs when the command finishes.
        Assert.True(observed.IsCancellationRequested);
    }

    public interface ICaptureTool
    {
        [CliRoute("capture")]
        [CliCommandExample("capture")]
        int Capture(CancellationToken cancellation);
    }

    private sealed class TokenCapturingTool(Action<CancellationToken> capture) : ICaptureTool
    {
        public int Capture(CancellationToken cancellation)
        {
            capture(cancellation);
            return 0;
        }
    }

    [Fact]
    public async Task Mount_A_Contract_Under_A_Root_Route()
    {
        // A fresh host per run: RunPorticoAsync starts and stops the host, and a stopped host's
        // ApplicationStopping token is already cancelled. One command, one process, one host.
        Assert.Equal(0, await MountedHost().RunPorticoAsync(["ops", "db", "migrate"]));
        Assert.Equal(2, await MountedHost().RunPorticoAsync(["db", "migrate"]));   // a mount is a move

        static IHost MountedHost() => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<Migrator>();
                services.AddPorticoCommands<IAdminTool, AdminTool>("ops");
            })
            .Build();
    }

    [Fact]
    public async Task Refuse_A_Second_Run_On_A_Stopped_Host()
    {
        // Not a limitation worth hiding: a CLI process runs one command, and the Generic Host is not
        // restartable. Running twice hands the second dispatch an already-cancelled token, so it
        // cancels immediately rather than silently doing half the work.
        using var host = Builder().Build();

        Assert.Equal(0, await host.RunPorticoAsync(["db", "migrate"]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.RunPorticoAsync(["db", "migrate"]));
    }

    [Fact]
    public async Task Run_Any_Hosted_Service_The_Service_Registered()
    {
        var worker = new RecordingHostedService();
        using var host = Builder(services => services.AddSingleton<IHostedService>(worker)).Build();

        await host.RunPorticoAsync(["db", "migrate"]);

        // The host is started and stopped around the dispatch. Documented, because a service whose
        // builder registers long-running workers will start them on every CLI invocation.
        Assert.True(worker.Started);
        Assert.True(worker.Stopped);
    }

    private sealed class RecordingHostedService : IHostedService
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Say_So_When_No_Contract_Was_Registered()
    {
        using var host = Host.CreateDefaultBuilder().Build();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.RunPorticoAsync(["db", "migrate"]));

        Assert.Contains("AddPorticoCommands", error.Message, StringComparison.Ordinal);
    }
}

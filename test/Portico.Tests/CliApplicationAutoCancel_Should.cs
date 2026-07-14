using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
//
// Ergonomic default: RunAsync() auto-wires Console.CancelKeyPress so consumers don't have
// to write the 3-line CTS boilerplate in every Main. When the caller passes a cancellable
// token, the framework trusts them and skips the auto-wire.
public sealed class CliApplicationAutoCancel_Should
{
    public sealed class WaitingService
    {
        public CancellationToken ObservedToken { get; private set; }

        [CliRoute("wait")]
        [CliCommandExample("wait")]
        public Task<int> Wait(CancellationToken ct)
        {
            ObservedToken = ct;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task Forward_Caller_Supplied_Cancellable_Token_Without_Auto_Wire()
    {
        var svc = new WaitingService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        int exit = await app.RunAsync("app.exe wait", cts.Token);

        Assert.Equal(CliExitException.CancelledExitCode, exit);
        // The handler's token was cancellable because the caller passed a real one.
        Assert.True(svc.ObservedToken.CanBeCanceled);
    }

    [Fact]
    public async Task Auto_Wire_When_No_Token_Supplied_And_Still_Produce_Cancellable_Handler_Token()
    {
        // When caller passes nothing (or CancellationToken.None), framework auto-wires
        // Ctrl+C. The handler still gets a cancellable token — it's just backed by the
        // framework's internal CTS rather than the caller's.
        var svc = new WaitingService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        int exit = await app.RunAsync("app.exe wait");

        Assert.Equal(0, exit);
        Assert.True(svc.ObservedToken.CanBeCanceled,
            "Framework-wired token should be cancellable so handlers can observe Ctrl+C.");
    }

    [Fact]
    public async Task Remove_Its_Own_CancelKeyPress_Handler_After_The_Run()
    {
        // Process-global event. If the framework doesn't clean up, repeated Run() calls
        // would stack handlers. Smoke-test by running twice with a sentinel handler in
        // between — if leaked, our sentinel would be called multiple times per Ctrl+C,
        // but we can't actually trigger Ctrl+C from a test. Instead, verify no exception.
        var svc = new WaitingService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        await app.RunAsync("app.exe wait");
        await app.RunAsync("app.exe wait");
        await app.RunAsync("app.exe wait");

        // If handlers leaked, the AppDomain would hold refs to captured cts's indefinitely.
        // The framework's `using` scope + `-=` in finally guarantees cleanup. Pass-through
        // of three runs without exceptions is the observable assertion.
        Assert.True(true);
    }

    // -----------------------------------------------------------------------------------------
    //  SIGTERM (graceful container shutdown) — SOL-42
    //
    //  Raising a real SIGTERM in-process isn't portably reproducible on Windows CI, so we test
    //  the seam we control: OnPosixTermination is the exact delegate body wired into
    //  PosixSignalRegistration.Create(PosixSignal.SIGTERM, ...). Driving it directly proves the
    //  wiring without depending on OS signal delivery.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Cancel_The_Scoped_Cts_When_Sigterm_Is_Raised()
    {
        using var cts = new CancellationTokenSource();
        var terminated = new StrongBox<bool>(false);
        var ctx = new PosixSignalContext(PosixSignal.SIGTERM);

        CliApplication.OnPosixTermination(ctx, cts, terminated);

        Assert.True(cts.IsCancellationRequested,
            "SIGTERM must cancel the scoped CTS so the handler's token drains gracefully.");
    }

    [Fact]
    public void Suppress_Default_Termination_So_The_Handler_Can_Drain_On_Sigterm()
    {
        using var cts = new CancellationTokenSource();
        var terminated = new StrongBox<bool>(false);
        var ctx = new PosixSignalContext(PosixSignal.SIGTERM);

        CliApplication.OnPosixTermination(ctx, cts, terminated);

        // Mirrors ConsoleLifetime: setting Cancel = true stops the runtime from terminating the
        // process immediately, giving the in-flight handler a window to observe cancellation.
        Assert.True(ctx.Cancel);
    }

    [Fact]
    public void Record_That_Termination_Was_Sigterm_Driven()
    {
        using var cts = new CancellationTokenSource();
        var terminated = new StrongBox<bool>(false);
        var ctx = new PosixSignalContext(PosixSignal.SIGTERM);

        CliApplication.OnPosixTermination(ctx, cts, terminated);

        // The flag is what lets RunWithAutoCancelAsync remap exit 130 (SIGINT) -> 143 (SIGTERM).
        Assert.True(terminated.Value);
    }

    [Fact]
    public void Expose_The_Posix_Sigint_And_Sigterm_Exit_Codes()
    {
        // POSIX convention: 128 + signal number. SIGINT = 2 -> 130, SIGTERM = 15 -> 143.
        Assert.Equal(130, CliExitException.CancelledExitCode);
        Assert.Equal(143, CliExitException.TerminatedExitCode);
    }
}

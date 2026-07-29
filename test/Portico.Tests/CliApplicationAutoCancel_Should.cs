using System;
using System.Linq;
using System.Reflection;
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

    /// <summary>
    /// <c>Console.CancelKeyPress</c> is a process-global event, and <c>CliApplication</c> is
    /// documented as reusable — a host may dispatch many commands over its lifetime. A subscription
    /// that outlives its run leaks the CTS it captured, once per invocation.
    /// </summary>
    /// <remarks>
    /// This used to end in <c>Assert.True(true)</c>, under a comment conceding that Ctrl+C cannot be
    /// raised from a test and settling for "verify no exception". But the three <c>await</c>s already
    /// fail the test on an exception, so the assertion added nothing — and a test named
    /// "Remove_Its_Own_CancelKeyPress_Handler" that stays green while every handler leaks is worse
    /// than no test, because its name is load-bearing in a way its body is not.
    /// <para>
    /// Raising the signal is indeed not possible here; counting the subscribers is. The event's
    /// backing delegate is a private static field on <see cref="Console"/>, so the invocation list is
    /// reachable by reflection, and
    /// <see cref="Observe_Its_Own_Subscriber_Counting_Mechanism_Working"/> proves that reading
    /// actually observes a subscription — an apparatus that silently returned 0 would make this test
    /// pass forever, which is the tautology it replaced wearing a different hat.
    /// </para>
    /// <para>
    /// The before/after comparison is only sound because <c>AssemblyInfo.cs</c> disables test
    /// parallelization assembly-wide: the count is process-global, so a concurrently running class
    /// that called <c>RunAsync</c> would move it under this test's feet. That setting exists for a
    /// different reason (the console is shared), and this test now depends on it too.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Remove_Its_Own_CancelKeyPress_Handler_After_The_Run()
    {
        var before = CancelKeyPressSubscriberCount();

        var svc = new WaitingService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        await app.RunAsync("app.exe wait");
        await app.RunAsync("app.exe wait");
        await app.RunAsync("app.exe wait");

        Assert.Equal(before, CancelKeyPressSubscriberCount());
    }

    /// <summary>
    /// Proves the measurement above can measure. If the runtime ever stops exposing a reachable
    /// backing delegate for <see cref="Console.CancelKeyPress"/>, this fails — and it fails
    /// <em>here</em>, saying the mechanism is broken, instead of making the leak test report a
    /// mysterious count.
    /// </summary>
    /// <remarks>
    /// A test whose apparatus is unverified is the tautology problem one level up: a
    /// <c>CancelKeyPressSubscriberCount</c> that always returned 0 would make the leak test pass
    /// forever. So the apparatus gets its own test.
    /// </remarks>
    [Fact]
    public void Observe_Its_Own_Subscriber_Counting_Mechanism_Working()
    {
        var before = CancelKeyPressSubscriberCount();

        ConsoleCancelEventHandler sentinel = (_, _) => { };
        Console.CancelKeyPress += sentinel;
        try
        {
            Assert.Equal(before + 1, CancelKeyPressSubscriberCount());
        }
        finally
        {
            Console.CancelKeyPress -= sentinel;
        }

        Assert.Equal(before, CancelKeyPressSubscriberCount());
    }

    /// <summary>
    /// How many handlers are subscribed to <see cref="Console.CancelKeyPress"/>. The event exposes
    /// only <c>add</c>/<c>remove</c>, so the count comes from the private static delegate behind it.
    /// </summary>
    /// <remarks>
    /// Located by field <em>type</em> rather than by name. The name is an implementation detail that
    /// differs nobody-knows-where — this suite runs on ubuntu and windows, and the first version of
    /// this helper hard-coded <c>s_cancelCallbacks</c> after checking one of them. A search for "the
    /// static field holding a <see cref="ConsoleCancelEventHandler"/>" is the property actually being
    /// relied on, and it survives a rename.
    /// </remarks>
    private static int CancelKeyPressSubscriberCount()
    {
        var candidates = typeof(Console)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(ConsoleCancelEventHandler))
            .ToArray();

        Assert.True(
            candidates.Length == 1,
            $"Expected exactly one private static ConsoleCancelEventHandler field on Console, found " +
            $"{candidates.Length}. This runtime does not expose the CancelKeyPress invocation list " +
            "where the test looks for it. Find the new shape — do not weaken the leak assertion back " +
            "to a pass-through, which is the state it shipped in.");

        return (candidates[0].GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
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

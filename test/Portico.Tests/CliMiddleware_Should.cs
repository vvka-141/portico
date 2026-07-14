using System;
using System.Collections.Generic;
using Xunit;

namespace Portico;

// ReSharper disable once InconsistentNaming
public sealed class CliMiddleware_Should
{
    public sealed class RecordingBundle : CliMiddleware
    {
        public List<string> Events { get; } = new();

        public override void OnExecutingAction(CliInvocation invocation)
        {
            Events.Add("OnExecutingAction");
            base.OnExecutingAction(invocation);
        }

        public override void OnActionExecuted(CliInvocation invocation)
        {
            Events.Add("OnActionExecuted");
            base.OnActionExecuted(invocation);
        }

        public override void OnError(CliInvocation invocation, Exception exception)
        {
            Events.Add($"OnError:{exception.GetType().Name}");
            base.OnError(invocation, exception);
        }
    }

    public sealed class HappyService
    {
        public bool BodyRan { get; private set; }

        [CliRoute("ok")]
        [CliCommandExample("ok")]
        public int Ok()
        {
            BodyRan = true;
            // Mark the body's position in the lifecycle order so the test can assert ordering.
            SharedSinkBundle.CurrentSink?.Add("Body");
            return 0;
        }
    }

    [Fact]
    public void Invoke_Lifecycle_In_Order_OnExecuting_Body_OnExecuted()
    {
        // Each Run() builds a fresh middleware (via Clone), so we record events on a middleware
        // we install via UseMiddleware and read post-invocation through… well, the recording
        // middleware's Events list is on the CLONE, not the original. We need a shared sink
        // to peek at the clone's behavior.
        var sink = new List<string>();
        var template = new SharedSinkBundle(sink);
        var svc = new HappyService();

        var app = CliApplication.Create(cfg => cfg
            .UseMiddleware(template)
            .AddCommands(svc));

        Assert.Equal(0, app.Run("app.exe ok"));
        Assert.True(svc.BodyRan);

        Assert.Equal(
            new[] { "OnExecutingAction", "Body", "OnActionExecuted" },
            sink);
    }

    public sealed class FailingService
    {
        [CliRoute("boom")]
        [CliCommandExample("boom")]
        public int Boom() => throw new InvalidOperationException("user-defined failure");
    }

    [Fact]
    public void Invoke_OnError_Between_Body_And_OnActionExecuted_When_Action_Throws()
    {
        // OnExecutingAction runs, then the body throws, OnError fires with the exception,
        // and OnActionExecuted still runs (cleanup armed before invocation, per S9 fix).
        var sink = new List<string>();
        var template = new SharedSinkBundle(sink);

        var app = CliApplication.Create(cfg => cfg
            .UseMiddleware(template)
            .AddCommands(new FailingService()));

        int exit = app.Run("app.exe boom");
        Assert.NotEqual(0, exit);

        Assert.Equal(
            new[] { "OnExecutingAction", "OnError:InvalidOperationException", "OnActionExecuted" },
            sink);
    }

    [Fact]
    public void Tolerate_Bundle_OnError_Hooks_That_Throw()
    {
        // A buggy telemetry hook should not mask the original failure. The processor swallows
        // hook-side exceptions and lets the original propagate to the SafeProcess catch path.
        var app = CliApplication.Create(cfg => cfg
            .UseMiddleware(new ThrowingErrorHookBundle())
            .AddCommands(new FailingService()));

        int exit = app.Run("app.exe boom");

        // Exit code reflects the original action failure, not the bundle's hook failure.
        Assert.Equal(CliExitException.RuntimeErrorExitCode, exit);
    }

    /// <summary>Bundle that pushes lifecycle events into a shared list — and bumps it from the
    /// <see cref="HappyService"/>'s body via the <c>"Body"</c> marker. Because each Process()
    /// clones the bundle, the shared sink lets the test see what the clone did.</summary>
    public sealed class SharedSinkBundle : CliMiddleware
    {
        public static List<string>? CurrentSink;

        public SharedSinkBundle(List<string> sink) { CurrentSink = sink; }

        public override void OnExecutingAction(CliInvocation invocation)
        {
            CurrentSink?.Add("OnExecutingAction");
        }

        public override void OnActionExecuted(CliInvocation invocation)
        {
            CurrentSink?.Add("OnActionExecuted");
        }

        public override void OnError(CliInvocation invocation, Exception exception)
        {
            CurrentSink?.Add($"OnError:{exception.GetType().Name}");
        }

        // The HappyService body adds the "Body" marker so the lifecycle order can be asserted.
        // But HappyService doesn't see the bundle directly — we wire the marker via a static
        // hook that the body invokes.
    }

    public sealed class ThrowingErrorHookBundle : CliMiddleware
    {
        public override void OnError(CliInvocation invocation, Exception exception)
        {
            throw new InvalidOperationException("buggy telemetry hook");
        }
    }
}

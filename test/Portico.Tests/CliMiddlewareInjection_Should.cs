using System;
using System.Collections.Generic;
using Portico.Testing;
using Xunit;

namespace Portico;

// POR-26. Analyzer POR006 (severity Error) refused to compile a CliMiddleware with a constructor
// dependency, claiming "middleware are instantiated per-invocation via Activator.CreateInstance".
// That premise is FALSE for middleware:
//
//   * CliMiddleware.Clone() is MemberwiseClone() — a shallow field copy, so an injected field
//     survives into every per-dispatch clone.
//   * Middleware only ever reaches the framework through UseMiddleware(instance), which the USER
//     constructs. The framework never news it up.
//
// It IS true for CliOptions bundles (CliOptionsParameterInfo really does Activator.CreateInstance),
// which is why POR006 still covers those. The rule conflated two lifecycles because CliMiddleware
// happens to inherit from CliOptions.
//
// This blocked POR-4's core shape: UseMiddleware(sp.GetRequiredService<AuditMiddleware>()).
public sealed class CliMiddlewareInjection_Should
{
    private interface IAuditLog
    {
        void Write(string message);
        IReadOnlyList<string> Entries { get; }
    }

    private sealed class AuditLog : IAuditLog
    {
        private readonly List<string> _entries = [];
        public void Write(string message) => _entries.Add(message);
        public IReadOnlyList<string> Entries => _entries;
    }

    // No public parameterless ctor. This is the shape a DI container produces.
    private sealed class AuditMiddleware(IAuditLog log) : CliMiddleware
    {
        public override void OnExecutingAction(CliInvocation invocation) => log.Write("before");
        public override void OnActionExecuted(CliInvocation invocation) => log.Write("after");
    }

    public interface ITool
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        int Run();
    }

    private sealed class Tool : ITool
    {
        public int Run()
        {
            Console.WriteLine("ran");
            return 0;
        }
    }

    [Fact]
    public void Run_A_Middleware_With_An_Injected_Service()
    {
        var audit = new AuditLog();

        var result = CliTestHarness
            .ForApplication(cfg => cfg
                .AddCommands(new Tool())
                .UseMiddleware(new AuditMiddleware(audit)))   // constructed by the caller, as DI would
            .Run("app.exe run");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ran", result.StandardOut, StringComparison.Ordinal);

        // The injected service survived the per-dispatch MemberwiseClone and both hooks fired.
        Assert.Equal(["before", "after"], audit.Entries);
    }

    [Fact]
    public void Share_The_Injected_Service_Across_Dispatches()
    {
        // The shallow clone SHARES reference-typed fields — which is precisely what makes an
        // injected, stateless service work. Documented on CliMiddleware; asserted here.
        var audit = new AuditLog();
        var harness = CliTestHarness.ForApplication(cfg => cfg
            .AddCommands(new Tool())
            .UseMiddleware(new AuditMiddleware(audit)));

        harness.Run("app.exe run");
        harness.Run("app.exe run");

        Assert.Equal(["before", "after", "before", "after"], audit.Entries);
    }
}

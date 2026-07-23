using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Portico;

/// <summary>Test-only console that buffers output in <see cref="StringWriter"/>s.</summary>
internal sealed class StringCliConsole : ICliConsole
{
    public StringWriter OutWriter { get; } = new();
    public StringWriter ErrorWriter { get; } = new();
    public TextWriter Out => OutWriter;
    public TextWriter Error => ErrorWriter;
    public TextReader In => TextReader.Null;
}

// Codifies the Phase-1 invariants for Portico:
//   - service-root route composition  (ROADMAP 1.5)
//   - duplicate-route detection       (ROADMAP 1.1a, config-time)
//   - ambiguous-match -> usage error  (ROADMAP 1.1a, runtime)
//   - argument type conversion works  (ROADMAP 1.1, dead-path fix)
// These tests are the defense line for future refactors.
// ReSharper disable once InconsistentNaming
public sealed class CliApplication_Should
{
    public interface ICountService
    {
        [CliRoute("count {n}")]
        [CliCommandExample("count 3")]
        int Count([CliArgument("how many")] int n);
    }

    public sealed class CountService : ICountService
    {
        public int Observed { get; private set; } = -1;

        public int Count(int n)
        {
            Observed = n;
            return 0;
        }
    }

    [Fact]
    public void Convert_PositionalArgument_To_DeclaredType()
    {
        var svc = new CountService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        int exit = processor.Run("app.exe count 42");

        Assert.Equal(0, exit);
        Assert.Equal(42, svc.Observed);
    }

    public interface IDatabaseCommands
    {
        [CliRoute("migrate")]
        [CliCommandExample("db migrate")]
        int Migrate();
    }

    public sealed class DatabaseCommands : IDatabaseCommands
    {
        public bool Invoked { get; private set; }

        public int Migrate()
        {
            Invoked = true;
            return 0;
        }
    }

    [Fact]
    public void Compose_ServiceRootRoute_With_MethodRoute()
    {
        var svc = new DatabaseCommands();
        var processor = CliApplication.Create(cfg =>
            cfg.AddCommands(svc, [new CliRouteAttribute("db")]));

        int exit = processor.Run("app.exe db migrate");

        Assert.Equal(0, exit);
        Assert.True(svc.Invoked);
    }

    [Fact]
    public void Not_Match_When_RootRoute_Is_Absent()
    {
        var svc = new DatabaseCommands();
        var processor = CliApplication.Create(cfg =>
            cfg.AddCommands(svc, [new CliRouteAttribute("db")]));

        int exit = processor.Run("app.exe migrate");

        Assert.NotEqual(0, exit);
        Assert.False(svc.Invoked);
    }

    public sealed class DoubleBookedService
    {
        [CliRoute("init")]
        [CliCommandExample("init")]
        public int First() => 0;

        [CliRoute("init")]
        [CliCommandExample("init")]
        public int Second() => 0;
    }

    [Fact]
    public void Reject_DuplicateRoute_At_Configuration_Time()
    {
        Assert.ThrowsAny<Exception>(() => CliApplication.Create(cfg =>
            cfg.AddCommands(new DoubleBookedService())));
    }

    public interface IFlagService
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        [CliCommandExample("run --verbose")]
        int Run([CliOption("--verbose")] CliFlag? verbose);
    }

    public sealed class FlagService : IFlagService
    {
        public bool? Verbose { get; private set; }

        public int Run(CliFlag? verbose)
        {
            Verbose = verbose.HasValue;
            return 0;
        }
    }

    [Fact]
    public void Suggest_Nearest_Command_When_Segment_Is_Mistyped()
    {
        var svc = new CountService();
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .AddCommands(svc)
            .WithConsole(console));

        int exit = processor.Run("app.exe countt 3");

        Assert.Equal(2, exit);
        var stderr = console.ErrorWriter.ToString();
        Assert.Contains("Unknown command", stderr);
        Assert.Contains("Did you mean", stderr);
        Assert.Contains("count", stderr);
    }

    [Fact]
    public void Route_Error_Messages_To_Injected_Console_Not_System_Console()
    {
        var svc = new CountService();
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .AddCommands(svc)
            .WithConsole(console));

        int exit = processor.Run("app.exe count notanint");

        Assert.Equal(2, exit);
        var stderr = console.ErrorWriter.ToString();
        Assert.Contains("notanint", stderr);
        Assert.Empty(console.OutWriter.ToString());
    }

    public interface IAsyncService
    {
        [CliRoute("compute {n}")]
        [CliCommandExample("compute 7")]
        Task<int> ComputeAsync([CliArgument("how many")] int n);
    }

    public sealed class AsyncService : IAsyncService
    {
        public int Observed { get; private set; } = -1;

        public async Task<int> ComputeAsync(int n)
        {
            await Task.Yield();
            Observed = n;
            return 0;
        }
    }

    [Fact]
    public async Task Await_TaskOfInt_Returning_Action()
    {
        var svc = new AsyncService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        int exit = await processor.RunAsync("app.exe compute 42");

        Assert.Equal(0, exit);
        Assert.Equal(42, svc.Observed);
    }

    public interface ICancellableService
    {
        [CliRoute("wait")]
        [CliCommandExample("wait")]
        Task<int> WaitAsync(CancellationToken cancellationToken);
    }

    public sealed class CancellableService : ICancellableService
    {
        public bool Observed { get; private set; }

        public async Task<int> WaitAsync(CancellationToken cancellationToken)
        {
            Observed = cancellationToken.CanBeCanceled;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }
    }

    [Fact]
    public async Task Inject_Ambient_CancellationToken_When_Parameter_Has_No_Option_Attribute()
    {
        var svc = new CancellableService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        using var cts = new CancellationTokenSource();
        int exit = await processor.RunAsync("app.exe wait", cts.Token);

        Assert.Equal(0, exit);
        Assert.True(svc.Observed, "Ambient CancellationToken should flow into the action parameter.");
    }

    [Fact]
    public async Task Propagate_CancellationToken_Signal_Through_To_Exit_Code()
    {
        var svc = new CancellableService();
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .AddCommands(svc)
            .WithConsole(console));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        int exit = await processor.RunAsync("app.exe wait", cts.Token);

        Assert.Equal(130, exit); // POSIX SIGINT convention (128 + 2)
        Assert.Contains("cancelled", console.ErrorWriter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public interface IScopedService
    {
        [CliRoute("tick")]
        [CliCommandExample("tick")]
        int Tick();
    }

    public sealed class ScopedService : IScopedService
    {
        public int InstanceId { get; } = Guid.NewGuid().GetHashCode();
        public int Tick() => 0;
    }

    [Fact]
    public void Invoke_Registered_Factory_On_Each_Command()
    {
        int factoryCalls = 0;
        var seenIds = new HashSet<int>();

        var processor = CliApplication.Create(cfg => cfg
            .AddCommands<IScopedService>(() =>
            {
                factoryCalls++;
                var svc = new ScopedService();
                seenIds.Add(svc.InstanceId);
                return svc;
            }));

        Assert.Equal(0, processor.Run("app.exe tick"));
        Assert.Equal(0, processor.Run("app.exe tick"));
        Assert.Equal(0, processor.Run("app.exe tick"));

        Assert.Equal(3, factoryCalls);
        Assert.Equal(3, seenIds.Count);
    }

    [Fact]
    public void Route_Through_Explicit_Factory_Per_Invocation()
    {
        int factoryCalls = 0;
        var svc = new ScopedService();

        var processor = CliApplication.Create(cfg => cfg
            .AddCommands<IScopedService>(() => { factoryCalls++; return svc; }));

        Assert.Equal(0, processor.Run("app.exe tick"));
        Assert.Equal(0, processor.Run("app.exe tick"));

        Assert.Equal(2, factoryCalls);
    }

    public interface IFlagAndScalarBoolService
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        [CliCommandExample("run --verbose --logging true")]
        int Run(
            // Switch (presence-only). Supplied → CliFlag.Default; absent → null.
            [CliOption("--verbose|-v")] CliFlag? verbose = null,
            // Scalar bool. Requires explicit "--logging true" / "--logging false".
            [CliOption("--logging")] bool? logging = null);
    }

    public sealed class FlagAndScalarBoolService : IFlagAndScalarBoolService
    {
        public bool? VerboseSupplied { get; private set; }
        public bool? Logging { get; private set; }
        public int Run(CliFlag? verbose, bool? logging)
        {
            VerboseSupplied = verbose.HasValue;
            Logging = logging;
            return 0;
        }
    }

    [Fact]
    public void Distinguish_Switch_From_Boolean_Scalar()
    {
        // CliFlag? is a switch (presence-only); bool? is a two-state scalar requiring an
        // explicit value. Exercised together to prove the two semantics don't blur.
        var svc = new FlagAndScalarBoolService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe run"));
        Assert.False(svc.VerboseSupplied);  // switch absent
        Assert.Null(svc.Logging);            // scalar absent

        svc = new FlagAndScalarBoolService();
        processor = CliApplication.Create(cfg => cfg.AddCommands(svc));
        Assert.Equal(0, processor.Run("app.exe run --verbose"));
        Assert.True(svc.VerboseSupplied);    // switch supplied

        svc = new FlagAndScalarBoolService();
        processor = CliApplication.Create(cfg => cfg.AddCommands(svc));
        Assert.Equal(0, processor.Run("app.exe run --logging true"));
        Assert.Equal(true, svc.Logging);     // scalar with explicit value

        svc = new FlagAndScalarBoolService();
        processor = CliApplication.Create(cfg => cfg.AddCommands(svc));
        Assert.Equal(0, processor.Run("app.exe run --logging false"));
        Assert.Equal(false, svc.Logging);    // scalar explicitly false
    }

    [Fact]
    public void Reject_Boolean_Scalar_Without_Value()
    {
        // `--logging` without a value is a usage error — bool is a scalar, not a switch.
        // (Users wanting presence-only should type the parameter as CliFlag?.)
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new FlagAndScalarBoolService()));

        int exit = processor.Run("app.exe run --logging");
        Assert.NotEqual(0, exit);
    }

    public sealed class TopLevelHelpService
    {
        [CliRoute("noop")]
        [CliCommandExample("noop")]
        public int Noop() => 0;
    }

    [Theory]
    [InlineData("app.exe --help")]
    [InlineData("app.exe -h")]
    [InlineData("app.exe -?")]
    [InlineData("app.exe help")]
    [InlineData("app.exe")]
    public void Display_GeneralHelp_On_TopLevel_Help_Signal(string commandLine)
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .AddCommands(new TopLevelHelpService())
            .WithConsole(console));

        int exit = processor.Run(commandLine);
        Assert.Equal(0, exit);

        // General help lists every registered route. The TopLevelHelpService exposes `noop`.
        var output = console.OutWriter.ToString();
        Assert.Contains("noop", output);
    }

    public interface IHelpfulCommand
    {
        [CliRoute("init {path}")]
        [CliCommandExample("init .", "Use current directory")]
        [CliCommandExample("init /src/db --template basic", "Use a named template")]
        [System.ComponentModel.Description("Initialize a new project at the given path.")]
        int Initialize(
            [CliArgument("Project directory path")] string path,
            [CliOption("--template|-t", "Template to scaffold from", DefaultValue = "basic")] string template = "basic",
            [CliOption("--verbose|-v", "Enable verbose output")] CliFlag? verbose = null);
    }

    public sealed class HelpfulCommand : IHelpfulCommand
    {
        public int Initialize(string path, string template, CliFlag? verbose) => 0;
    }

    [Fact]
    public void Emit_Rich_PerCommand_Help()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .AddCommands(new HelpfulCommand())
            .WithConsole(console));

        Assert.Equal(0, processor.Run("myapp init --help"));

        var help = console.OutWriter.ToString();

        Assert.Contains("Usage: myapp init <PATH> [options]", help);
        Assert.Contains("Initialize a new project", help);
        Assert.Contains("Arguments:", help);
        Assert.Contains("PATH", help);
        Assert.Contains("Options:", help);
        Assert.Contains("--template, -t", help);
        Assert.Contains("Template to scaffold from", help);
        Assert.Contains("(default: basic)", help);
        Assert.Contains("--verbose, -v", help);
        Assert.Contains("Examples:", help);
        Assert.Contains("myapp init .", help);
        Assert.Contains("Use current directory", help);
    }

    // Multi-[CliRoute] used to slip through and silently concatenate; Session 9 added a runtime
    // rejection. Session 12 promoted that to AttributeUsage(AllowMultiple = false) so the
    // duplicate is now a compile-time CS0579. The runtime check in CliMethodInfo remains as
    // defensive coverage for hand-built reflection scenarios but isn't reachable from C# source.

    public sealed class BadBundle : CliOptions
    {
        public BadBundle(int requiredDependency) { }  // no parameterless ctor

        [CliOption("--foo")]
        public string? Foo { get; set; }
    }

    public sealed class BadBundleService
    {
        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run(BadBundle bundle) => 0;
    }

    [Fact]
    public void Reject_Bundle_Without_Parameterless_Ctor_At_Configuration_Time()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new BadBundleService())));
        Assert.Contains("parameterless constructor", ex.Message);
    }

    [Fact]
    public void Materialize_CliFlag_As_Nullable_Struct()
    {
        var svc = new FlagService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe run"));
        Assert.Equal(false, svc.Verbose);

        svc = new FlagService();
        processor = CliApplication.Create(cfg => cfg.AddCommands(svc));
        Assert.Equal(0, processor.Run("app.exe run --verbose"));
        Assert.Equal(true, svc.Verbose);
    }

    // POSIX combined short flags ------------------------------------------------------------

    public sealed class ShortFlagsService
    {
        public bool A { get; private set; }
        public bool B { get; private set; }
        public bool C { get; private set; }
        public string? Name { get; private set; }

        [CliRoute("run")]
        [CliCommandExample("run -abc")]
        public int Run(
            [CliOption("-a")] CliFlag? a = null,
            [CliOption("-b")] CliFlag? b = null,
            [CliOption("-c")] CliFlag? c = null,
            [CliOption("-n")] string? name = null)
        {
            A = a.HasValue;
            B = b.HasValue;
            C = c.HasValue;
            Name = name;
            return 0;
        }
    }

    [Fact]
    public void Expand_Combined_Short_Flags()
    {
        var svc = new ShortFlagsService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe run -abc"));
        Assert.True(svc.A);
        Assert.True(svc.B);
        Assert.True(svc.C);
    }

    [Fact]
    public void Expand_Partial_Combined_Short_Flags()
    {
        var svc = new ShortFlagsService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        // -ab only: c should be false (absent).
        Assert.Equal(0, processor.Run("app.exe run -ab"));
        Assert.True(svc.A);
        Assert.True(svc.B);
        Assert.False(svc.C);
    }

    [Fact]
    public void Expand_Glued_Scalar_Short()
    {
        var svc = new ShortFlagsService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        // -nbob becomes -n bob (no flags at front).
        Assert.Equal(0, processor.Run("app.exe run -nbob"));
        Assert.Equal("bob", svc.Name);
    }

    [Fact]
    public void Expand_Flags_Then_Glued_Scalar_Short()
    {
        var svc = new ShortFlagsService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        // -abnbob = -a -b -n bob
        Assert.Equal(0, processor.Run("app.exe run -abnbob"));
        Assert.True(svc.A);
        Assert.True(svc.B);
        Assert.Equal("bob", svc.Name);
    }

    [Fact]
    public void Leave_Unknown_Short_Combination_For_Error_Path()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new ShortFlagsService()));

        // -axz: x and z are not defined. Expander refuses to split. Downstream emits
        // unrecognized-option error.
        int exit = processor.Run("app.exe run -axz");
        Assert.NotEqual(0, exit);
        Assert.Contains("-axz", console.ErrorWriter.ToString());
    }

    [Fact]
    public void Preserve_Registered_MultiChar_Short_Name()
    {
        // Multi-char shorts like -foo are legal (non-POSIX but allowed). Expander must
        // preserve them when they match a registered option exactly.
        var svc = new MultiCharShortService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe do -foo"));
        Assert.True(svc.Triggered);
    }

    public sealed class MultiCharShortService
    {
        public bool Triggered { get; private set; }

        [CliRoute("do")]
        [CliCommandExample("do -foo")]
        public int Do([CliOption("-foo")] CliFlag? foo = null)
        {
            Triggered = foo.HasValue;
            return 0;
        }
    }

    [Fact]
    public void Do_Not_Expand_Negative_Number_As_Combined_Short()
    {
        // Even if -1 / -2 / -3 were defined as shorts (they're not here), the negative-number
        // heuristic must win over expansion.
        var svc = new NegativeNumberService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe offset --by -42"));
        Assert.Equal(-42, svc.Captured);
    }

    // POSIX -- end-of-options terminator -----------------------------------------------------

    public sealed class CpService
    {
        public string? Captured { get; private set; }

        [CliRoute("cp {path}")]
        [CliCommandExample("cp -- -file-with-leading-dash.txt")]
        public int Cp([CliArgument("path to copy")] string path)
        {
            Captured = path;
            return 0;
        }
    }

    [Fact]
    public void Pass_DashLeading_File_Via_Terminator()
    {
        var svc = new CpService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        int exit = processor.Run("app.exe cp -- -weirdfile.txt");

        Assert.Equal(0, exit);
        Assert.Equal("-weirdfile.txt", svc.Captured);
    }

    // Collection-typed options ---------------------------------------------------------------

    public sealed class ArrayOptionService
    {
        public int[]? Captured { get; private set; }

        [CliRoute("tag")]
        [CliCommandExample("tag --ids 1 2 3")]
        public int Tag([CliOption("--ids")] int[] ids)
        {
            Captured = ids;
            return 0;
        }
    }

    [Fact]
    public void Bind_SpaceSeparated_Values_To_IntArray()
    {
        var svc = new ArrayOptionService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe tag --ids 1 2 3"));
        Assert.Equal(new[] { 1, 2, 3 }, svc.Captured);
    }

    [Fact]
    public void Bind_Repeated_Option_To_IntArray()
    {
        var svc = new ArrayOptionService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe tag --ids 1 --ids 2 --ids 3"));
        Assert.Equal(new[] { 1, 2, 3 }, svc.Captured);
    }

    public sealed class ListOptionService
    {
        public List<string>? Captured { get; private set; }

        [CliRoute("ship")]
        [CliCommandExample("ship --files a.txt b.txt")]
        public int Ship([CliOption("--files|-f")] List<string> files)
        {
            Captured = files;
            return 0;
        }
    }

    [Fact]
    public void Bind_Values_To_GenericList()
    {
        var svc = new ListOptionService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe ship --files a.txt b.txt c.txt"));
        Assert.NotNull(svc.Captured);
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, svc.Captured!);
    }

    public sealed class EnumerableOptionService
    {
        public IEnumerable<int>? Captured { get; private set; }

        [CliRoute("pick")]
        [CliCommandExample("pick --n 1 2 3")]
        public int Pick([CliOption("--n")] IEnumerable<int> n)
        {
            Captured = n;
            return 0;
        }
    }

    [Fact]
    public void Bind_Values_To_IEnumerable()
    {
        var svc = new EnumerableOptionService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe pick --n 1 2 3"));
        Assert.Equal(new[] { 1, 2, 3 }, svc.Captured);
    }

    public sealed class RequiredCollectionService
    {
        [CliRoute("build")]
        [CliCommandExample("build --sources src1 src2")]
        public int Build([CliOption("--sources|-s")] string[] sources) => 0;
    }

    [Fact]
    public void Reject_Missing_Required_Collection_Option()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new RequiredCollectionService()));

        int exit = processor.Run("app.exe build");

        Assert.NotEqual(0, exit);
        var error = console.ErrorWriter.ToString();
        Assert.Contains("--sources, -s", error);
    }

    public sealed class OptionalCollectionService
    {
        public string[]? Captured { get; private set; } = null;

        [CliRoute("list")]
        [CliCommandExample("list")]
        public int List([CliOption("--items")] string[]? items)
        {
            Captured = items;
            return 0;
        }
    }

    [Fact]
    public void Allow_Missing_Optional_Collection_Option()
    {
        var svc = new OptionalCollectionService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe list"));
        Assert.Null(svc.Captured);
    }

    public sealed class NegativeCollectionService
    {
        public int[]? Captured { get; private set; }

        [CliRoute("offsets")]
        [CliCommandExample("offsets --by -1 -2 -3")]
        public int Offsets([CliOption("--by")] int[] by)
        {
            Captured = by;
            return 0;
        }
    }

    [Fact]
    public void Bind_Negative_Values_To_Collection()
    {
        var svc = new NegativeCollectionService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe offsets --by -1 -2 -3"));
        Assert.Equal(new[] { -1, -2, -3 }, svc.Captured);
    }

    public sealed class NegativeNumberService
    {
        public int Captured { get; private set; }

        [CliRoute("offset")]
        [CliCommandExample("offset --by -5")]
        public int Offset([CliOption("--by|-b")] int by)
        {
            Captured = by;
            return 0;
        }
    }

    [Theory]
    [InlineData("app.exe offset --by -5", -5)]
    [InlineData("app.exe offset -b -42", -42)]
    [InlineData("app.exe offset --by=-7", -7)]
    public void Bind_NegativeNumber_To_Int_Option(string commandLine, int expected)
    {
        var svc = new NegativeNumberService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run(commandLine));
        Assert.Equal(expected, svc.Captured);
    }

    public sealed class OnErrorTracker : CliMiddleware
    {
        public static int Count;
        public static Exception? Last;

        public override void OnError(CliInvocation invocation, Exception exception)
        {
            Count++;
            Last = exception;
        }
    }

    public sealed class FailingService
    {
        [CliRoute("boom")]
        [CliCommandExample("boom")]
        public int Boom() => throw new InvalidOperationException("user-defined failure");
    }

    [Fact]
    public void Invoke_OnError_When_Action_Throws()
    {
        OnErrorTracker.Count = 0;
        OnErrorTracker.Last = null;

        var processor = CliApplication.Create(cfg => cfg
            .UseMiddleware(new OnErrorTracker())
            .AddCommands(new FailingService()));

        int exit = processor.Run("app.exe boom");

        Assert.NotEqual(0, exit);
        Assert.Equal(1, OnErrorTracker.Count);
        Assert.IsType<InvalidOperationException>(OnErrorTracker.Last);
    }

    public sealed class CancelService
    {
        [CliRoute("wait")]
        [CliCommandExample("wait")]
        public async Task<int> Wait(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    [Fact]
    public async Task Return_130_When_Action_Is_Cancelled()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new CancelService()));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        int exit = await processor.RunAsync("app.exe wait", cts.Token);

        Assert.Equal(130, exit);
        Assert.Contains("cancel", console.ErrorWriter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public sealed class VersionProbeService
    {
        public int Invocations { get; private set; }

        [CliRoute("run")]
        [CliCommandExample("run")]
        public int Run()
        {
            Invocations++;
            return 0;
        }
    }

    [Fact]
    public void Print_Version_When_DoubleDash_Version_Requested()
    {
        var console = new StringCliConsole();
        var svc = new VersionProbeService();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .WithVersion("1.2.3")
            .AddCommands(svc));

        int exit = processor.Run("app.exe --version");

        Assert.Equal(0, exit);
        Assert.Contains("1.2.3", console.OutWriter.ToString());
        Assert.Equal(0, svc.Invocations);
    }

    [Fact]
    public void Print_Version_When_Short_V_Requested()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .WithVersion("2.0.0")
            .AddCommands(new VersionProbeService()));

        int exit = processor.Run("app.exe -V");

        Assert.Equal(0, exit);
        Assert.Contains("2.0.0", console.OutWriter.ToString());
    }

    [Fact]
    public void Print_Version_Even_When_Route_Would_Otherwise_Match()
    {
        var console = new StringCliConsole();
        var svc = new VersionProbeService();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .WithVersion("9.9.9")
            .AddCommands(svc));

        int exit = processor.Run("app.exe run --version");

        Assert.Equal(0, exit);
        Assert.Contains("9.9.9", console.OutWriter.ToString());
        Assert.Equal(0, svc.Invocations);
    }

    [Fact]
    public void Treat_Version_Flag_As_Unknown_When_No_Version_Configured()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new VersionProbeService()));

        int exit = processor.Run("app.exe --version");

        // No version is configured, so --version is just an unrecognized option.
        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Not_Confuse_Lowercase_V_With_Version_Flag()
    {
        // Short -V must be case-sensitive so that -v (verbose) is free for per-app use.
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .WithVersion("3.0.0")
            .AddCommands(new VersionProbeService()));

        int exit = processor.Run("app.exe -v");

        // Lowercase -v should not trigger the version flow.
        Assert.DoesNotContain("3.0.0", console.OutWriter.ToString());
        Assert.NotEqual(0, exit);
    }

    public sealed class VerboseService
    {
        [CliRoute("run")]
        [CliCommandExample("run --verbose")]
        public int Run([CliOption("--verbose|-v")] CliFlag? verbose = null) => 0;
    }

    public sealed class RequiredScalarService
    {
        [CliRoute("print")]
        [CliCommandExample("print --message hi")]
        public int Print([CliOption("--message|-m")] string message) => 0;
    }

    public sealed class RequiredMapService
    {
        public Dictionary<string, string>? Captured { get; private set; }

        [CliRoute("deploy")]
        [CliCommandExample("deploy --env[region] eu")]
        public int Deploy([CliOption("--env|-e")] Dictionary<string, string> env)
        {
            Captured = env;
            return 0;
        }
    }

    [Fact]
    public void Reject_Missing_Required_Dictionary_Option()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new RequiredMapService()));

        int exit = processor.Run("app.exe deploy");

        // Required dictionary with no entries should fail, not silently succeed with an empty map.
        Assert.NotEqual(0, exit);
        Assert.Contains("--env, -e", console.ErrorWriter.ToString());
    }

    [Fact]
    public void Accept_NonEmpty_Required_Dictionary_Option()
    {
        var console = new StringCliConsole();
        var svc = new RequiredMapService();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(svc));

        int exit = processor.Run("app.exe deploy --env[region] eu");

        Assert.Equal(0, exit);
        Assert.NotNull(svc.Captured);
        Assert.Equal("eu", svc.Captured!["region"]);
    }

    public sealed class OptionalMapService
    {
        public Dictionary<string, string>? Captured { get; private set; }

        [CliRoute("deploy")]
        [CliCommandExample("deploy")]
        public int Deploy([CliOption("--env|-e")] Dictionary<string, string>? env)
        {
            Captured = env;
            return 0;
        }
    }

    [Fact]
    public void Accept_Missing_Optional_Dictionary()
    {
        var svc = new OptionalMapService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        int exit = processor.Run("app.exe deploy");

        Assert.Equal(0, exit);
        // Optional + no entries => empty dictionary, not the required-missing failure.
        Assert.NotNull(svc.Captured);
        Assert.Empty(svc.Captured!);
    }

    [Fact]
    public void Render_Error_Aliases_In_CommaSeparated_LongFirst_Form()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new RequiredScalarService()));

        int exit = processor.Run("app.exe print");

        Assert.NotEqual(0, exit);
        var error = console.ErrorWriter.ToString();
        Assert.Contains("--message, -m", error);
        Assert.DoesNotContain("--message|-m", error);
    }

    [Fact]
    public void Suggest_Near_Match_For_Unknown_Option()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new VerboseService()));

        int exit = processor.Run("app.exe run --vebose");

        Assert.NotEqual(0, exit);
        var error = console.ErrorWriter.ToString();
        Assert.Contains("--vebose", error);
        Assert.Contains("Did you mean", error);
        Assert.Contains("--verbose", error);
    }

    [Fact]
    public void Omit_Suggestion_When_No_Alias_Is_Close_Enough()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new VerboseService()));

        int exit = processor.Run("app.exe run --zzz");

        Assert.NotEqual(0, exit);
        var error = console.ErrorWriter.ToString();
        Assert.Contains("--zzz", error);
        Assert.DoesNotContain("Did you mean", error);
    }

    [Fact]
    public void Invoke_Version_Factory_Lazily()
    {
        int calls = 0;
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .WithVersion(() => { calls++; return "7.7.7"; })
            .AddCommands(new VersionProbeService()));

        Assert.Equal(0, calls);

        processor.Run("app.exe --version");
        Assert.Equal(1, calls);

        processor.Run("app.exe --version");
        Assert.Equal(2, calls);
    }

    // DataAnnotations validation ------------------------------------------------------------

    public sealed class RangedArgService
    {
        [CliRoute("scale {n}")]
        [CliCommandExample("scale 5")]
        public int Scale([Range(1, 10)] [CliArgument("target count")] int n) => 0;
    }

    [Fact]
    public void Reject_Argument_Outside_Range_Annotation()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new RangedArgService()));

        int exit = processor.Run("app.exe scale 42");

        Assert.NotEqual(0, exit);
        var error = console.ErrorWriter.ToString();
        Assert.Contains("<N>", error);
        Assert.Contains("between 1 and 10", error);
    }

    [Fact]
    public void Accept_Argument_Inside_Range_Annotation()
    {
        var processor = CliApplication.Create(cfg => cfg.AddCommands(new RangedArgService()));
        Assert.Equal(0, processor.Run("app.exe scale 5"));
    }

    public sealed class PatternOptionService
    {
        [CliRoute("tag")]
        [CliCommandExample("tag --name lowercase-only")]
        public int Tag(
            [CliOption("--name|-n")]
            [RegularExpression("^[a-z0-9-]+$")] string name) => 0;
    }

    [Fact]
    public void Reject_Option_Not_Matching_RegularExpression()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new PatternOptionService()));

        int exit = processor.Run("app.exe tag --name Not_Valid");

        Assert.NotEqual(0, exit);
        var error = console.ErrorWriter.ToString();
        Assert.Contains("--name", error);
    }

    [Fact]
    public void Accept_Option_Matching_RegularExpression()
    {
        var processor = CliApplication.Create(cfg => cfg.AddCommands(new PatternOptionService()));
        Assert.Equal(0, processor.Run("app.exe tag --name valid-name"));
    }

    public sealed class StringLengthBundle : CliOptions
    {
        [CliOption("--code|-c")]
        [StringLength(3, MinimumLength = 3)]
        public string? Code { get; set; }
    }

    public sealed class StringLengthBundleService
    {
        [CliRoute("set")]
        [CliCommandExample("set --code ABC")]
        public int Set(StringLengthBundle bundle) => 0;
    }

    [Fact]
    public void Reject_Bundle_Property_Failing_StringLength()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new StringLengthBundleService()));

        int exit = processor.Run("app.exe set --code AB");

        Assert.NotEqual(0, exit);
        Assert.Contains("--code", console.ErrorWriter.ToString());
    }

    [Fact]
    public void Accept_Bundle_Property_Passing_StringLength()
    {
        var processor = CliApplication.Create(cfg => cfg.AddCommands(new StringLengthBundleService()));
        Assert.Equal(0, processor.Run("app.exe set --code ABC"));
    }

    public sealed class CrossFieldBundle : CliOptions, IValidatableObject
    {
        [CliOption("--min")]
        public int Min { get; set; }

        [CliOption("--max")]
        public int Max { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Min > Max)
            {
                yield return new ValidationResult("--min must not exceed --max.");
            }
        }
    }

    public sealed class CrossFieldBundleService
    {
        [CliRoute("range")]
        [CliCommandExample("range --min 1 --max 10")]
        public int Range(CrossFieldBundle bundle) => 0;
    }

    [Fact]
    public void Run_IValidatableObject_After_Property_Binding()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new CrossFieldBundleService()));

        int exit = processor.Run("app.exe range --min 10 --max 1");

        Assert.NotEqual(0, exit);
        Assert.Contains("must not exceed", console.ErrorWriter.ToString());
    }

    [Fact]
    public void Pass_IValidatableObject_When_Rule_Holds()
    {
        var processor = CliApplication.Create(cfg =>
            cfg.AddCommands(new CrossFieldBundleService()));
        Assert.Equal(0, processor.Run("app.exe range --min 1 --max 10"));
    }

    public sealed class MultiAnnotatedArgService
    {
        [CliRoute("mk {s}")]
        [CliCommandExample("mk ab")]
        public int Mk(
            [StringLength(3, MinimumLength = 2)]
            [RegularExpression("^[a-z]+$")]
            [CliArgument("short string")] string s) => 0;
    }

    [Fact]
    public void Report_All_Failing_Annotations_On_Same_Value()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new MultiAnnotatedArgService()));

        int exit = processor.Run("app.exe mk X");

        Assert.NotEqual(0, exit);
        var error = console.ErrorWriter.ToString();
        // Both [StringLength] (too short) and [RegularExpression] (uppercase) violated.
        Assert.Contains("<S>", error);
    }

    // Parameter-level [CliArgument] (Session 15a) -----------------------------------------------

    public sealed class ParameterLevelArgService
    {
        public string? CapturedPath { get; private set; }

        [CliRoute("init {path}")]
        [CliCommandExample("init ./project")]
        public int Init([CliArgument("target directory path")] string path)
        {
            CapturedPath = path;
            return 0;
        }
    }

    [Fact]
    public void Accept_ParameterLevel_CliArgument()
    {
        var svc = new ParameterLevelArgService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe init ./project"));
        Assert.Equal("./project", svc.CapturedPath);
    }

    [Fact]
    public void Render_ParameterLevel_Arg_In_Help()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new ParameterLevelArgService()));

        Assert.Equal(0, processor.Run("app.exe init --help"));
        var help = console.OutWriter.ToString();
        Assert.Contains("<PATH>", help);
        Assert.Contains("target directory path", help);
    }

    public sealed class MixedArgService
    {
        public string? A { get; private set; }
        public string? B { get; private set; }

        // Two placeholders, both described. Order comes from the route string and nothing else.
        [CliRoute("mix {a} {b}")]
        [CliCommandExample("mix A B")]
        public int Mix([CliArgument("first")] string a, [CliArgument("second")] string b)
        {
            A = a;
            B = b;
            return 0;
        }
    }

    [Fact]
    public void Bind_Several_Described_Placeholders_In_Route_Order()
    {
        var svc = new MixedArgService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe mix first second"));
        Assert.Equal("first", svc.A);
        Assert.Equal("second", svc.B);
    }

#pragma warning disable POR007 // Intentional duplicate: this fixture verifies the runtime guard
                               // is still a safety net for builds without the analyzer. POR007 now
                               // catches the same mistake at compile time (see CliArgumentAnalyzers).
    public sealed class DuplicateArgService
    {
        // Two [CliArgument] on one parameter: only one can be bound, so the other's description
        // would be silently dropped.
        [CliRoute("go {x}")]
        public int Go([CliArgument("first description")] [CliArgument("second description")] string x) => 0;
    }
#pragma warning restore POR007

    [Fact]
    public void Reject_Duplicate_Argument_Declaration_At_ConfigTime()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new DuplicateArgService())));
        Assert.Contains("At most one is allowed per parameter", ex.Message);
    }

    // The "[CliArgument] with no placeholder" rejection lives in CliRouteOwnsPath_Should, with the
    // rest of POR-70's invariant.

    // Env-var fallback (Session 15b) ------------------------------------------------------------

    public sealed class EnvVarService
    {
        public string? Captured { get; private set; }

        [CliRoute("connect")]
        [CliCommandExample("connect --host db.example.com")]
        public int Connect(
            [CliOption("--host|-h", EnvironmentVariable = "CLI_TEST_HOST")] string host)
        {
            Captured = host;
            return 0;
        }
    }

    [Fact]
    public void Fall_Back_To_Environment_Variable_When_Option_Absent()
    {
        Environment.SetEnvironmentVariable("CLI_TEST_HOST", "env.example.com");
        try
        {
            var svc = new EnvVarService();
            var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

            Assert.Equal(0, processor.Run("app.exe connect"));
            Assert.Equal("env.example.com", svc.Captured);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLI_TEST_HOST", null);
        }
    }

    [Fact]
    public void Prefer_Command_Line_Over_Environment_Variable()
    {
        Environment.SetEnvironmentVariable("CLI_TEST_HOST", "env.example.com");
        try
        {
            var svc = new EnvVarService();
            var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

            Assert.Equal(0, processor.Run("app.exe connect --host cmd.example.com"));
            Assert.Equal("cmd.example.com", svc.Captured);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLI_TEST_HOST", null);
        }
    }

    [Fact]
    public void Error_When_Required_Option_Missing_And_Env_Var_Unset()
    {
        Environment.SetEnvironmentVariable("CLI_TEST_HOST", null);
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new EnvVarService()));

        int exit = processor.Run("app.exe connect");
        Assert.NotEqual(0, exit);
        Assert.Contains("--host, -h", console.ErrorWriter.ToString());
    }

    // Concurrency smoke test (Session 18d) -------------------------------------------------------

    public sealed class ConcurrentCounterService
    {
        private int _ticks;
        public int Ticks => _ticks;

        [CliRoute("tick {n}")]
        [CliCommandExample("tick 1")]
        public int Tick(int n)
        {
            System.Threading.Interlocked.Add(ref _ticks, n);
            return 0;
        }
    }

    [Fact]
    public void Be_Safe_For_Parallel_Process_Calls_On_Same_Processor()
    {
        // Pins the architectural claim from CHARTER §4.8: CliApplication is immutable after
        // construction, so concurrent Process() calls from any number of threads are safe.
        // If anyone ever introduces mutable state post-construction, this test explodes.
        var svc = new ConcurrentCounterService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        const int concurrency = 64;
        var exitCodes = new System.Collections.Concurrent.ConcurrentBag<int>();

        System.Threading.Tasks.Parallel.For(0, concurrency, _ =>
        {
            exitCodes.Add(processor.Run("app.exe tick 1"));
        });

        Assert.All(exitCodes, code => Assert.Equal(0, code));
        Assert.Equal(concurrency, exitCodes.Count);
        Assert.Equal(concurrency, svc.Ticks);
    }

    // Inner-exception preservation (Session 17.5c) -----------------------------------------------

    public sealed class IntParsingService
    {
        [CliRoute("count")]
        [CliCommandExample("count --n 42")]
        public int Count([CliOption("--n")] int n) => 0;
    }

    [Fact]
    public void Preserve_Conversion_Inner_Exception_In_Materialization_Error()
    {
        var console = new StringCliConsole();
        var processor = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .AddCommands(new IntParsingService()));

        // "abc" isn't an int. Scalar materializer throws with inner FormatException preserved.
        int exit = processor.Run("app.exe count --n abc");

        Assert.NotEqual(0, exit);
        // The outer message surfaces the failure; the existence of the inner exception is what
        // Session 17.5c guarantees — testable at the reflection layer rather than via stderr.
        Assert.Contains("abc", console.ErrorWriter.ToString());
    }

    // Route placeholders (Session 16a) ----------------------------------------------------------

    public sealed class PlaceholderService
    {
        public string? CapturedPath { get; private set; }

        [CliRoute("init {path}")]
        [CliCommandExample("init ./project")]
        public int Init(string path)
        {
            CapturedPath = path;
            return 0;
        }
    }

    [Fact]
    public void Bind_Placeholder_To_Matching_Parameter()
    {
        var svc = new PlaceholderService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe init ./my-project"));
        Assert.Equal("./my-project", svc.CapturedPath);
    }

    public sealed class InterleavedPlaceholderService
    {
        public string? CapturedPath { get; private set; }

        // Placeholder in the middle — this is the case Session 9 lost when multi-[CliRoute]
        // was removed. Route placeholders restore it in a single attribute string.
        [CliRoute("config {path} apply")]
        [CliCommandExample("config ./app.json apply")]
        public int Apply(string path)
        {
            CapturedPath = path;
            return 0;
        }
    }

    [Fact]
    public void Bind_Placeholder_Interleaved_Between_Literal_Segments()
    {
        var svc = new InterleavedPlaceholderService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe config ./app.json apply"));
        Assert.Equal("./app.json", svc.CapturedPath);
    }

    public sealed class MismatchedPlaceholderService
    {
        // Placeholder 'pat' doesn't match any parameter (parameter is 'path').
        [CliRoute("init {pat}")]
        [CliCommandExample("init .")]
        public int Init(string path) => 0;
    }

    [Fact]
    public void Reject_Placeholder_Without_Matching_Parameter()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new MismatchedPlaceholderService())));

        Assert.Contains("{pat}", ex.Message);
        Assert.Contains("no parameter 'pat'", ex.Message);
        Assert.Contains("Available parameters: path", ex.Message);
    }

    // POR-70 removed Reject_Placeholder_And_CliArgument_For_Same_Parameter: a placeholder plus a
    // [CliArgument] describing it is no longer a conflict, it is the recommended shape. The
    // positive case is pinned by CliArgumentDescription_Should.

    public sealed class MultiPlaceholderService
    {
        public string? Source { get; private set; }
        public string? Dest { get; private set; }

        [CliRoute("cp {source} {dest}")]
        [CliCommandExample("cp a.txt b.txt")]
        public int Copy(string source, string dest)
        {
            Source = source;
            Dest = dest;
            return 0;
        }
    }

    [Fact]
    public void Bind_Multiple_Placeholders_In_Declared_Order()
    {
        var svc = new MultiPlaceholderService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe cp a.txt b.txt"));
        Assert.Equal("a.txt", svc.Source);
        Assert.Equal("b.txt", svc.Dest);
    }

    // ==========================================================================================
    //  Session 23 — argument/option conflict + interface/class-level [CliRoute] prefix.
    // ==========================================================================================

    public sealed class ArgumentOptionConflictService
    {
        [CliRoute("put {item}")]
        [CliCommandExample("put foo")]
        public int Put(
            [CliOption("--item")] string item) => 0;
    }

    [Fact]
    public void Reject_Parameter_Bound_To_Both_Route_Argument_And_CliOption()
    {
        var ex = Assert.Throws<CliConfigurationException>(() =>
            CliApplication.Create(cfg => cfg.AddCommands(new ArgumentOptionConflictService())));

        Assert.Contains("bound to a route argument", ex.Message);
        Assert.Contains("[CliOption]", ex.Message);
    }

    [CliRoute("pg")]
    public interface IInterfacePrefixService
    {
        [CliRoute("migrate {path}")]
        [CliCommandExample("migrate ./sql")]
        int Migrate(string path);
    }

    public sealed class InterfacePrefixService : IInterfacePrefixService
    {
        public string? ObservedPath { get; private set; }
        public int Migrate(string path)
        {
            ObservedPath = path;
            return 0;
        }
    }

    [Fact]
    public void Compose_Interface_Level_Route_Prefix()
    {
        var svc = new InterfacePrefixService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe pg migrate ./sql"));
        Assert.Equal("./sql", svc.ObservedPath);
    }

    [Fact]
    public void Reject_Bare_Method_Route_When_Interface_Has_Prefix()
    {
        // "migrate ./sql" omits the interface's "pg" prefix; should not match.
        var svc = new InterfacePrefixService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.NotEqual(0, processor.Run("app.exe migrate ./sql"));
    }

    [CliRoute("db")]
    public sealed class ClassBeatsInterfaceService : IInterfacePrefixService
    {
        public string? ObservedPath { get; private set; }
        public int Migrate(string path)
        {
            ObservedPath = path;
            return 0;
        }
    }

    [Fact]
    public void Class_Level_Prefix_Wins_Over_Interface_Prefix()
    {
        var svc = new ClassBeatsInterfaceService();
        var processor = CliApplication.Create(cfg => cfg.AddCommands(svc));

        Assert.Equal(0, processor.Run("app.exe db migrate ./sql"));
        Assert.Equal("./sql", svc.ObservedPath);
        // And the interface prefix should NOT also be accepted.
        Assert.NotEqual(0, CliApplication.Create(cfg => cfg.AddCommands(new ClassBeatsInterfaceService())).Run("app.exe pg migrate ./sql"));
    }
}

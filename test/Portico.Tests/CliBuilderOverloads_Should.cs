using System;
using Xunit;

namespace Portico;

// SOL-81: example-as-tests that close the §6.5 "proof" gap for three builder overloads kept as
// downstream library surface (they had no internal caller — the normal state for a library).
// ReSharper disable once InconsistentNaming
public sealed class CliBuilderOverloads_Should
{
    public sealed class NoopService
    {
        [CliRoute("noop")]
        [CliCommandExample("noop")]
        public int Noop() => 0;
    }

    public sealed class DbTool
    {
        public bool Ran { get; private set; }

        [CliRoute("migrate")]
        [CliCommandExample("migrate")]
        public int Migrate()
        {
            Ran = true;
            return 0;
        }
    }

    // (1) ICliApplicationBuilder.WithVersion(Version) — BCL-native convenience.
    [Fact]
    public void WithVersion_from_a_System_Version_prints_it()
    {
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .WithVersion(new Version(1, 4, 0))
            .AddCommands(new NoopService()));

        Assert.Equal(0, app.Run("app.exe --version"));
        Assert.Contains("1.4.0", console.OutWriter.ToString());
    }

    // (2) ICliApplicationBuilder.AddCommands<T>(Func<T>, rootRoutes) — factory + root-route prefix.
    [Fact]
    public void AddCommands_factory_with_root_routes_prefixes_the_route()
    {
        DbTool? made = null;
        var app = CliApplication.Create(cfg => cfg
            .AddCommands<DbTool>(() => made = new DbTool(), new[] { new CliRouteAttribute("db") }));

        Assert.Equal(0, app.Run("app.exe db migrate"));   // "db" prefix + "migrate" route
        Assert.NotNull(made);
        Assert.True(made!.Ran);
    }

    // (3) CliVersionBuilder.Text(Func<string>) composed with custom Triggers — the combination that
    // WithVersion(Func<string>) alone cannot express (kept-both rationale in WORKLOG).
    [Fact]
    public void VersionBuilder_Text_factory_composes_with_custom_triggers()
    {
        var console = new StringCliConsole();
        var calls = 0;
        var app = CliApplication.Create(cfg => cfg
            .WithConsole(console)
            .WithVersion(v => v.Text(() => $"dyn {++calls}").Triggers("ver"))
            .AddCommands(new NoopService()));

        Assert.Equal(0, app.Run("app.exe ver"));   // custom subcommand-style version trigger
        Assert.Contains("dyn 1", console.OutWriter.ToString());
    }
}

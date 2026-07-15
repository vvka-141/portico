using Xunit;

namespace Portico;

// A [CliOption(DefaultValue = X)] on a CliOptions bundle property must bind X when the option is
// omitted — exactly as it does on a plain method parameter. Before POR-59, CliOptionsPropertyInfo
// passed null to the materializer instead of the resolved default, so the bundle silently kept its
// Activator-created type default (0 / null) while help still advertised "(default: X)". Both a
// value-typed and a reference-typed property are covered — the value-typed case is the one the old
// GetValue(bundle) fallback masked, because 0 is not null.
// ReSharper disable once InconsistentNaming
public sealed class CliBundleDefault_Should
{
    public sealed class DefaultsBundle : CliOptions
    {
        [CliOption("--rows|-r", DefaultValue = "42")]
        public int Rows { get; set; }

        [CliOption("--label|-l", DefaultValue = "none")]
        public string? Label { get; set; }
    }

    public sealed class ReportService
    {
        public DefaultsBundle? Captured { get; private set; }

        [CliRoute("report")]
        [CliCommandExample("report --rows 10 --label q3")]
        public int Report(DefaultsBundle bundle)
        {
            Captured = bundle;
            return 0;
        }
    }

    private static ReportService Run(string commandLine)
    {
        var svc = new ReportService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));
        Assert.Equal(0, app.Run(commandLine));
        return svc;
    }

    [Fact]
    public void Apply_Value_Typed_Default_When_Omitted()
    {
        var svc = Run("app.exe report");

        Assert.NotNull(svc.Captured);
        Assert.Equal(42, svc.Captured!.Rows);   // not 0, the int type default
    }

    [Fact]
    public void Apply_Reference_Typed_Default_When_Omitted()
    {
        var svc = Run("app.exe report");

        Assert.NotNull(svc.Captured);
        Assert.Equal("none", svc.Captured!.Label);   // not null, the string type default
    }

    [Fact]
    public void Let_An_Explicit_Value_Override_The_Default()
    {
        var svc = Run("app.exe report --rows 10 --label q3");

        Assert.NotNull(svc.Captured);
        Assert.Equal(10, svc.Captured!.Rows);
        Assert.Equal("q3", svc.Captured!.Label);
    }

    [Fact]
    public void Apply_The_Default_Only_To_The_Omitted_Property()
    {
        var svc = Run("app.exe report --rows 7");

        Assert.NotNull(svc.Captured);
        Assert.Equal(7, svc.Captured!.Rows);
        Assert.Equal("none", svc.Captured!.Label);   // default still applied to the omitted one
    }
}

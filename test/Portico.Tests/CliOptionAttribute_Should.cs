using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Xunit;

namespace Portico;

// POR-109. Direct tests for CliOptionAttribute's virtual seams: subclass conversion (CanAccept),
// comparer override (GetValueComparer), AllowsCsv, and bundle-property binding. Previously ~64%
// coverage; the untested paths are the documented extension points.
// ReSharper disable once InconsistentNaming
public sealed class CliOptionAttribute_Should
{
    // ---- AllowsCsv ----

    [Fact]
    public void Default_AllowsCsv_to_true()
    {
        var attr = new CliOptionAttribute("--items");
        Assert.True(attr.AllowsCsv);
    }

    [Fact]
    public void Allow_subclass_to_disable_csv()
    {
        var attr = new NoCsvOptionAttribute("--items");
        Assert.False(attr.AllowsCsv);
    }

    // ---- GetValueComparer ----

    [Fact]
    public void Default_comparer_is_Ordinal()
    {
        var attr = new CliOptionAttribute("--cfg");
        Assert.Same(StringComparer.Ordinal, attr.GetValueComparer());
    }

    [Fact]
    public void Allow_subclass_to_override_comparer()
    {
        var attr = new CaseInsensitiveOptionAttribute("--cfg");
        Assert.Same(StringComparer.OrdinalIgnoreCase, attr.GetValueComparer());
    }

    [Fact]
    public void Wire_overridden_comparer_into_map_binding()
    {
        var svc = new CaseInsensitiveMapService();
        var console = new StringCliConsole();
        var app = CliApplication.Create(cfg => cfg.WithConsole(console).AddCommands(svc));

        var exit = app.Run("app.exe config --cfg[Region] eu --cfg[REGION] us");

        Assert.Equal(CliExitException.UsageErrorExitCode, exit);
        Assert.Contains("Duplicate key", console.ErrorWriter.ToString());
    }

    [Fact]
    public void Keep_default_comparer_case_sensitive_for_map_binding()
    {
        var svc = new CaseSensitiveMapService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        var exit = app.Run("app.exe defaults --cfg[Region] eu --cfg[REGION] us");

        Assert.Equal(0, exit);
        Assert.NotNull(svc.Captured);
        Assert.Equal(2, svc.Captured!.Count);
    }

    // ---- CanAccept subclass override ----

    [Fact]
    public void Accept_custom_type_via_subclass_converter()
    {
        var svc = new CoordinateService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        var exit = app.Run("app.exe plot --point 3.5,7.2");

        Assert.Equal(0, exit);
        Assert.NotNull(svc.CapturedPoint);
        Assert.Equal(3.5, svc.CapturedPoint!.Value.X);
        Assert.Equal(7.2, svc.CapturedPoint!.Value.Y);
    }

    // ---- Bundle-property binding ----

    [Fact]
    public void Bind_bundle_properties_from_the_command_line()
    {
        var svc = new BundleService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        var exit = app.Run("app.exe deploy --target staging --port 9090");

        Assert.Equal(0, exit);
        Assert.NotNull(svc.CapturedOpts);
        Assert.Equal("staging", svc.CapturedOpts!.Target);
        Assert.Equal(9090, svc.CapturedOpts.Port);
    }

    [Fact]
    public void Apply_bundle_property_defaults_when_omitted()
    {
        var svc = new BundleService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        var exit = app.Run("app.exe deploy --target prod");

        Assert.Equal(0, exit);
        Assert.NotNull(svc.CapturedOpts);
        Assert.Equal("prod", svc.CapturedOpts!.Target);
        Assert.Equal(8080, svc.CapturedOpts.Port);
    }

    [Fact]
    public void Bind_custom_subclass_attribute_on_bundle_property()
    {
        var svc = new CustomBundleService();
        var app = CliApplication.Create(cfg => cfg.AddCommands(svc));

        var exit = app.Run("app.exe locate --origin 1.0,2.0");

        Assert.Equal(0, exit);
        Assert.NotNull(svc.CapturedOpts);
        Assert.Equal(1.0, svc.CapturedOpts!.Origin.X);
        Assert.Equal(2.0, svc.CapturedOpts!.Origin.Y);
    }

    // ---- Supporting types ----

    private sealed class NoCsvOptionAttribute : CliOptionAttribute
    {
        public NoCsvOptionAttribute(string specification, string description = "")
            : base(specification, description) { }

        public override bool AllowsCsv => false;
    }

    private sealed class CaseInsensitiveOptionAttribute : CliOptionAttribute
    {
        public CaseInsensitiveOptionAttribute(string specification, string description = "")
            : base(specification, description) { }

        public override StringComparer GetValueComparer() => StringComparer.OrdinalIgnoreCase;
    }

    public sealed class CaseInsensitiveMapService
    {
        [CliRoute("config")]
        [CliCommandExample("config --cfg[key] value")]
        public int Configure([CaseInsensitiveOption("--cfg")] Dictionary<string, string> cfg)
        {
            return 0;
        }
    }

    public sealed class CaseSensitiveMapService
    {
        public IReadOnlyDictionary<string, string>? Captured { get; private set; }

        [CliRoute("defaults")]
        [CliCommandExample("defaults --cfg[key] value")]
        public int Configure([CliOption("--cfg")] Dictionary<string, string> cfg)
        {
            Captured = cfg;
            return 0;
        }
    }

    public readonly struct Coordinate
    {
        public double X { get; }
        public double Y { get; }

        public Coordinate(double x, double y) { X = x; Y = y; }
    }

    [TypeConverter(typeof(CoordinateConverter))]
    public readonly struct TaggedCoordinate
    {
        public double X { get; }
        public double Y { get; }

        public TaggedCoordinate(double x, double y) { X = x; Y = y; }
    }

    private sealed class CoordinateConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string s)
            {
                var parts = s.Split(',');
                return new TaggedCoordinate(
                    double.Parse(parts[0], CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture));
            }
            return base.ConvertFrom(context, culture, value);
        }
    }

    private sealed class CoordinateOptionAttribute : CliOptionAttribute
    {
        public CoordinateOptionAttribute(string specification, string description = "")
            : base(specification, description) { }

        public override bool CanAccept(Type optionType, out TypeConverter converter)
        {
            if (optionType == typeof(Coordinate))
            {
                converter = new CoordinateForCoordinateOptionConverter();
                return true;
            }
            return base.CanAccept(optionType, out converter);
        }

        private sealed class CoordinateForCoordinateOptionConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
                sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

            public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
            {
                if (value is string s)
                {
                    var parts = s.Split(',');
                    return new Coordinate(
                        double.Parse(parts[0], CultureInfo.InvariantCulture),
                        double.Parse(parts[1], CultureInfo.InvariantCulture));
                }
                return base.ConvertFrom(context, culture, value);
            }
        }
    }

    public sealed class CoordinateService
    {
        public Coordinate? CapturedPoint { get; private set; }

        [CliRoute("plot")]
        [CliCommandExample("plot --point 0,0")]
        public int Plot([CoordinateOption("--point")] Coordinate point)
        {
            CapturedPoint = point;
            return 0;
        }
    }

    public sealed class DeployOptions : CliOptions
    {
        [CliOption("--target|-t")]
        public string Target { get; set; } = "";

        [CliOption("--port|-p", DefaultValue = "8080")]
        public int Port { get; set; }
    }

    public sealed class BundleService
    {
        public DeployOptions? CapturedOpts { get; private set; }

        [CliRoute("deploy")]
        [CliCommandExample("deploy --target prod --port 8080")]
        public int Deploy(DeployOptions opts)
        {
            CapturedOpts = opts;
            return 0;
        }
    }

    public sealed class LocationOptions : CliOptions
    {
        [CoordinateOption("--origin")]
        public Coordinate Origin { get; set; }
    }

    public sealed class CustomBundleService
    {
        public LocationOptions? CapturedOpts { get; private set; }

        [CliRoute("locate")]
        [CliCommandExample("locate --origin 0,0")]
        public int Locate(LocationOptions opts)
        {
            CapturedOpts = opts;
            return 0;
        }
    }
}

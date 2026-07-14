using System;
using System.Reflection;
using Xunit;

namespace Portico.Reflection;

public sealed class ParameterInfoDecorator_Should
{
    private static readonly ParameterInfo Parameter =
        typeof(Subject).GetMethod(nameof(Subject.Build))!.GetParameters()[0];

    private static readonly ParameterInfo OptionalParameter =
        typeof(Subject).GetMethod(nameof(Subject.Build))!.GetParameters()[1];

    [Fact]
    public void DelegateToTheWrappedParameter()
    {
        var decorator = new PassThrough(Parameter);

        Assert.Equal("source", decorator.Name);
        Assert.Equal(typeof(string), decorator.ParameterType);
        Assert.Equal(0, decorator.Position);
        Assert.False(decorator.IsOptional);
        Assert.Equal(Parameter.GetHashCode(), decorator.GetHashCode());
    }

    [Fact]
    public void SurfaceOptionalityAndDefaults()
    {
        var decorator = new PassThrough(OptionalParameter);

        Assert.True(decorator.IsOptional);
        Assert.True(decorator.HasDefaultValue);
        Assert.Equal(1, decorator.DefaultValue);
    }

    [Fact]
    public void ConvertImplicitlyToTheWrappedParameter()
    {
        ParameterInfo unwrapped = new PassThrough(Parameter);

        Assert.Same(Parameter, unwrapped);
    }

    [Fact]
    public void SurfaceCustomAttributes()
    {
        var decorator = new PassThrough(Parameter);

        Assert.True(decorator.IsDefined(typeof(MarkerAttribute), inherit: false));
        Assert.NotNull(decorator.GetCustomAttribute<MarkerAttribute>(inherit: false));
    }

    [Fact]
    public void AllowADerivedTypeToOverrideOneMember()
    {
        var decorator = new Renamed(Parameter);

        Assert.Equal("SOURCE", decorator.Name);
        Assert.Equal(typeof(string), decorator.ParameterType);
    }

    private sealed class PassThrough(ParameterInfo parameter) : ParameterInfoDecorator(parameter);

    private sealed class Renamed(ParameterInfo parameter) : ParameterInfoDecorator(parameter)
    {
        public override string? Name => base.Name?.ToUpperInvariant();
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    private sealed class MarkerAttribute : Attribute;

    private sealed class Subject
    {
        public void Build([Marker] string source, int retries = 1) => _ = (source, retries);
    }
}

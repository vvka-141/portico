using System;
using System.Reflection;
using Xunit;

namespace Portico.Reflection;

public sealed class PropertyInfoDecorator_Should
{
    private static readonly PropertyInfo Property =
        typeof(Subject).GetProperty(nameof(Subject.Verbose))!;

    [Fact]
    public void DelegateToTheWrappedProperty()
    {
        var decorator = new PassThrough(Property);

        Assert.Equal("Verbose", decorator.Name);
        Assert.Equal(typeof(bool), decorator.PropertyType);
        Assert.Equal(typeof(Subject), decorator.DeclaringType);
        Assert.True(decorator.CanRead);
        Assert.True(decorator.CanWrite);
        Assert.Equal(Property.GetHashCode(), decorator.GetHashCode());
    }

    [Fact]
    public void ConvertImplicitlyToTheWrappedProperty()
    {
        PropertyInfo unwrapped = new PassThrough(Property);

        Assert.Same(Property, unwrapped);
    }

    [Fact]
    public void ReadAndWriteTheValue()
    {
        var decorator = new PassThrough(Property);
        var subject = new Subject();

        decorator.SetValue(subject, true);

        Assert.True(subject.Verbose);
        Assert.Equal(true, decorator.GetValue(subject));
    }

    [Fact]
    public void SurfaceCustomAttributes()
    {
        var decorator = new PassThrough(Property);

        Assert.True(decorator.IsDefined(typeof(MarkerAttribute)));
        Assert.NotNull(decorator.GetCustomAttribute<MarkerAttribute>());
    }

    [Fact]
    public void AllowADerivedTypeToOverrideOneMember()
    {
        var decorator = new Renamed(Property);

        Assert.Equal("verbose", decorator.Name);
        Assert.Equal(typeof(bool), decorator.PropertyType);
    }

    private sealed class PassThrough(PropertyInfo property) : PropertyInfoDecorator(property);

    private sealed class Renamed(PropertyInfo property) : PropertyInfoDecorator(property)
    {
        public override string Name => base.Name.ToLowerInvariant();
    }

    [AttributeUsage(AttributeTargets.Property)]
    private sealed class MarkerAttribute : Attribute;

    private sealed class Subject
    {
        [Marker]
        public bool Verbose { get; set; }
    }
}

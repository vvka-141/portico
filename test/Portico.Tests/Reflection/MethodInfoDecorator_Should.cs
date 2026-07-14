using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Portico.Reflection;

public sealed class MethodInfoDecorator_Should
{
    private static readonly MethodInfo Method =
        typeof(Subject).GetMethod(nameof(Subject.Build))!;

    [Fact]
    public void DelegateToTheWrappedMethod()
    {
        var decorator = new PassThrough(Method);

        Assert.Equal("Build", decorator.Name);
        Assert.Equal(typeof(int), decorator.ReturnType);
        Assert.Equal(typeof(Subject), decorator.DeclaringType);
        Assert.True(decorator.IsPublic);
        Assert.False(decorator.IsStatic);
        Assert.Single(decorator.GetParameters());
        Assert.Equal(Method.ToString(), decorator.ToString());
        Assert.Equal(Method.GetHashCode(), decorator.GetHashCode());
    }

    [Fact]
    public void ConvertImplicitlyToTheWrappedMethod()
    {
        MethodInfo unwrapped = new PassThrough(Method);

        Assert.Same(Method, unwrapped);
    }

    [Fact]
    public void Invoke()
    {
        var decorator = new PassThrough(Method);

        Assert.Equal(42, decorator.Invoke(new Subject(), [21]));
    }

    [Fact]
    public void SurfaceCustomAttributes()
    {
        var decorator = new PassThrough(Method);

        Assert.True(decorator.IsDefined(typeof(MarkerAttribute), inherit: false));
        Assert.Single(decorator.GetCustomAttributes<MarkerAttribute>(inherit: false));
    }

    [Fact]
    public void AllowADerivedTypeToOverrideOneMember()
    {
        // The whole point of the decorator: change Name, inherit everything else untouched.
        var decorator = new Renamed(Method);

        Assert.Equal("build", decorator.Name);
        Assert.Equal(typeof(int), decorator.ReturnType);
    }

    private sealed class PassThrough(MethodInfo method) : MethodInfoDecorator(method);

    private sealed class Renamed(MethodInfo method) : MethodInfoDecorator(method)
    {
        public override string Name => base.Name.ToLowerInvariant();
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class MarkerAttribute : Attribute;

    private sealed class Subject
    {
        [Marker]
        public int Build(int value) => value * 2;
    }
}

using System;
using System.ComponentModel;
using Xunit;

namespace Portico;

// Default-value resolution exercised in isolation, without constructing a CliOptionAttribute — the
// virtual CanAccept seam is supplied as a delegate (SOL-79).
// ReSharper disable once InconsistentNaming
public sealed class CliOptionDefaultResolver_Should
{
    // Stand-in for the attribute's CanAccept seam: accept anything TypeConverter can parse.
    private static bool StandardConverter(Type type, out TypeConverter converter)
    {
        converter = TypeDescriptor.GetConverter(type);
        return converter.CanConvertFrom(typeof(string));
    }

    private static bool RejectAll(Type type, out TypeConverter converter)
    {
        converter = null!;
        return false;
    }

    [Fact]
    public void Report_required_when_not_nullable_and_no_default()
    {
        var optional = CliOptionDefaultResolver.Resolve(
            typeof(int), "parameter", "n",
            isNullable: false, hasReflectedDefault: false, reflectedDefault: null,
            attributeDefault: null, tryGetConverter: StandardConverter, out var dv);

        Assert.False(optional);
        Assert.Null(dv);
    }

    [Fact]
    public void Prefer_the_reflected_default_over_the_attribute_default()
    {
        var optional = CliOptionDefaultResolver.Resolve(
            typeof(int), "parameter", "n",
            isNullable: false, hasReflectedDefault: true, reflectedDefault: 5,
            attributeDefault: "9", tryGetConverter: StandardConverter, out var dv);

        Assert.True(optional);
        Assert.Equal(5, dv);
    }

    [Fact]
    public void Treat_nullable_without_default_as_optional_with_null()
    {
        var optional = CliOptionDefaultResolver.Resolve(
            typeof(string), "parameter", "name",
            isNullable: true, hasReflectedDefault: false, reflectedDefault: null,
            attributeDefault: null, tryGetConverter: StandardConverter, out var dv);

        Assert.True(optional);
        Assert.Null(dv);
    }

    [Fact]
    public void Convert_the_attribute_default_via_the_converter()
    {
        var optional = CliOptionDefaultResolver.Resolve(
            typeof(int), "parameter", "retries",
            isNullable: false, hasReflectedDefault: false, reflectedDefault: null,
            attributeDefault: "3", tryGetConverter: StandardConverter, out var dv);

        Assert.True(optional);
        Assert.Equal(3, dv);
    }

    [Fact]
    public void Throw_config_error_when_the_attribute_default_will_not_convert() =>
        Assert.Throws<CliConfigurationException>(() => CliOptionDefaultResolver.Resolve(
            typeof(int), "parameter", "retries",
            isNullable: false, hasReflectedDefault: false, reflectedDefault: null,
            attributeDefault: "notanumber", tryGetConverter: StandardConverter, out _));

    [Fact]
    public void Throw_config_error_when_the_type_is_incompatible() =>
        Assert.Throws<CliConfigurationException>(() => CliOptionDefaultResolver.Resolve(
            typeof(int), "parameter", "retries",
            isNullable: false, hasReflectedDefault: false, reflectedDefault: null,
            attributeDefault: "3", tryGetConverter: RejectAll, out _));
}

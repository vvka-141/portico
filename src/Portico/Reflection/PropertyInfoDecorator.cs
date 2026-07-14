using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Portico.Reflection;

/// <summary>
/// Base class for decorators around <see cref="PropertyInfo"/>. Delegates every member to the
/// wrapped instance so a derived type overrides only what it means to change.
/// </summary>
internal abstract class PropertyInfoDecorator(PropertyInfo property)
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly PropertyInfo _property = property;

    /// <summary>
    /// Lets a decorator be passed anywhere the underlying <see cref="PropertyInfo"/> is expected.
    /// </summary>
    public static implicit operator PropertyInfo(PropertyInfoDecorator decorator) => decorator._property;

    public override string ToString() => _property.ToString() ?? string.Empty;

    public override bool Equals(object? obj) => _property.Equals(obj);

    public override int GetHashCode() => _property.GetHashCode();

    public virtual Type PropertyType => _property.PropertyType;
    public virtual string Name => _property.Name;
    public virtual Type? DeclaringType => _property.DeclaringType;
    public virtual Type? ReflectedType => _property.ReflectedType;
    public virtual bool CanRead => _property.CanRead;
    public virtual bool CanWrite => _property.CanWrite;
    public virtual PropertyAttributes Attributes => _property.Attributes;
    public virtual MethodInfo? GetMethod => _property.GetMethod;
    public virtual MethodInfo? SetMethod => _property.SetMethod;
    public virtual IEnumerable<CustomAttributeData> CustomAttributes => _property.CustomAttributes;

    public virtual object? GetValue(object? obj) => _property.GetValue(obj);

    public virtual object? GetValue(object? obj, object?[]? index) =>
        _property.GetValue(obj, index);

    public virtual void SetValue(object? obj, object? value) =>
        _property.SetValue(obj, value);

    public virtual void SetValue(object? obj, object? value, object?[]? index) =>
        _property.SetValue(obj, value, index);

    public virtual MethodInfo[] GetAccessors(bool nonPublic = false) =>
        _property.GetAccessors(nonPublic);

    public virtual MethodInfo? GetGetMethod(bool nonPublic = false) =>
        _property.GetGetMethod(nonPublic);

    public virtual MethodInfo? GetSetMethod(bool nonPublic = false) =>
        _property.GetSetMethod(nonPublic);

    public virtual object? GetCustomAttribute(Type attributeType, bool inherit = false) =>
        _property.GetCustomAttribute(attributeType, inherit);

    public virtual T? GetCustomAttribute<T>(bool inherit = false) where T : Attribute =>
        _property.GetCustomAttribute<T>(inherit);

    public virtual object[] GetCustomAttributes(bool inherit = false) =>
        _property.GetCustomAttributes(inherit);

    public virtual object[] GetCustomAttributes(Type attributeType, bool inherit = false) =>
        _property.GetCustomAttributes(attributeType, inherit);

    public virtual IEnumerable<T> GetCustomAttributes<T>(bool inherit = false) where T : Attribute =>
        _property.GetCustomAttributes<T>(inherit);

    public virtual bool IsDefined(Type attributeType, bool inherit = false) =>
        _property.IsDefined(attributeType, inherit);

    public virtual Type[] GetRequiredCustomModifiers() =>
        _property.GetRequiredCustomModifiers();

    public virtual Type[] GetOptionalCustomModifiers() =>
        _property.GetOptionalCustomModifiers();

    public virtual ParameterInfo[] GetIndexParameters() =>
        _property.GetIndexParameters();
}

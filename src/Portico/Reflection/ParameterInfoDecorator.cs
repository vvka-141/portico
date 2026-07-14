using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Portico.Reflection;

/// <summary>
/// Base class for decorators around <see cref="ParameterInfo"/>, which is abstract and cannot
/// be subclassed usefully. Delegates every member to the wrapped instance so a derived type
/// overrides only what it means to change.
/// </summary>
internal abstract class ParameterInfoDecorator(ParameterInfo parameter)
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly ParameterInfo _parameter = parameter;

    /// <summary>
    /// Lets a decorator be passed anywhere the underlying <see cref="ParameterInfo"/> is expected.
    /// </summary>
    public static implicit operator ParameterInfo(ParameterInfoDecorator decorator) => decorator._parameter;

    public override string ToString() => _parameter.ToString() ?? string.Empty;

    public override bool Equals(object? obj) => _parameter.Equals(obj);

    public override int GetHashCode() => _parameter.GetHashCode();

    public virtual Type ParameterType => _parameter.ParameterType;
    public virtual string? Name => _parameter.Name;
    public virtual int Position => _parameter.Position;
    public virtual bool IsIn => _parameter.IsIn;
    public virtual bool IsOut => _parameter.IsOut;
    public virtual bool IsOptional => _parameter.IsOptional;
    public virtual bool HasDefaultValue => _parameter.HasDefaultValue;
    public virtual object? DefaultValue => _parameter.DefaultValue;
    public virtual MemberInfo Member => _parameter.Member;
    public virtual IEnumerable<CustomAttributeData> CustomAttributes => _parameter.CustomAttributes;

    public virtual object? GetCustomAttribute(Type attributeType, bool inherit) =>
        _parameter.GetCustomAttribute(attributeType, inherit);

    public virtual T? GetCustomAttribute<T>(bool inherit) where T : Attribute =>
        _parameter.GetCustomAttribute<T>(inherit);

    public virtual object[] GetCustomAttributes(bool inherit) =>
        _parameter.GetCustomAttributes(inherit);

    public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) =>
        _parameter.GetCustomAttributes(attributeType, inherit);

    public virtual IEnumerable<T> GetCustomAttributes<T>(bool inherit) where T : Attribute =>
        _parameter.GetCustomAttributes<T>(inherit);

    public virtual bool IsDefined(Type attributeType, bool inherit) =>
        _parameter.IsDefined(attributeType, inherit);
}

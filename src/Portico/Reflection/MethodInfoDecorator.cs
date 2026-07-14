using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Portico.Reflection;

/// <summary>
/// Base class for decorators around <see cref="MethodInfo"/>. Delegates every member to the
/// wrapped instance so a derived type overrides only what it means to change.
/// </summary>
internal abstract class MethodInfoDecorator(MethodInfo method)
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly MethodInfo _method = method;

    /// <summary>
    /// Lets a decorator be passed anywhere the underlying <see cref="MethodInfo"/> is expected.
    /// </summary>
    public static implicit operator MethodInfo(MethodInfoDecorator decorator) => decorator._method;

    public override string ToString() => _method.ToString() ?? string.Empty;

    public override bool Equals(object? obj) => _method.Equals(obj);

    public override int GetHashCode() => _method.GetHashCode();

    public virtual string Name => _method.Name;
    public virtual Type? DeclaringType => _method.DeclaringType;
    public virtual Type ReturnType => _method.ReturnType;
    public virtual MethodAttributes Attributes => _method.Attributes;
    public virtual bool IsStatic => _method.IsStatic;
    public virtual bool IsPublic => _method.IsPublic;
    public virtual bool IsAbstract => _method.IsAbstract;
    public virtual bool IsVirtual => _method.IsVirtual;
    public virtual IEnumerable<CustomAttributeData> CustomAttributes => _method.CustomAttributes;

    public virtual ParameterInfo[] GetParameters() => _method.GetParameters();

    [DebuggerStepThrough]
    public virtual object? Invoke(object? obj, object?[]? parameters) => _method.Invoke(obj, parameters);

    public virtual Type[] GetGenericArguments() => _method.GetGenericArguments();

    public virtual bool IsDefined(Type attributeType, bool inherit) =>
        _method.IsDefined(attributeType, inherit);

    public virtual IEnumerable<T> GetCustomAttributes<T>(bool inherit) where T : Attribute =>
        _method.GetCustomAttributes<T>(inherit);

    public virtual object[] GetCustomAttributes(bool inherit) =>
        _method.GetCustomAttributes(inherit);

    public virtual object[] GetCustomAttributes(Type attributeType, bool inherit) =>
        _method.GetCustomAttributes(attributeType, inherit);
}

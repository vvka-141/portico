using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;

namespace Portico.Reflection;

/// <summary>
/// The reflection helpers the option binder needs. Internal: not Portico's public surface.
/// </summary>
internal static class ReflectionExtensions
{
    private const string NullableAttributeFullName = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttributeFullName = "System.Runtime.CompilerServices.NullableContextAttribute";

    /// <example><code>
    /// bool optional = typeof(User).GetProperty("MiddleName").IsNullable();
    /// </code></example>
    public static bool IsNullable(this PropertyInfo self)
    {
        if (self.PropertyType.IsValueType &&
            Nullable.GetUnderlyingType(self.PropertyType) is not null)
        {
            return true;
        }

        return CheckNullableAttributes(self);
    }

    /// <example><code>
    /// bool optional = method.GetParameters()[0].IsNullable();
    /// </code></example>
    public static bool IsNullable(this ParameterInfo self)
    {
        if (self.ParameterType.IsValueType &&
            Nullable.GetUnderlyingType(self.ParameterType) is not null)
        {
            return true;
        }

        return CheckNullableAttributes(self);
    }

    /// <summary>
    /// Inspects nullable-reference-type metadata on a member or parameter. Uses
    /// <see cref="CustomAttributeData"/> (name-based lookup) rather than runtime
    /// <see cref="Attribute"/> instances: the C# compiler embeds
    /// <c>System.Runtime.CompilerServices.NullableAttribute</c> as an internal type <em>per
    /// assembly</em>, so <c>attribute is NullableAttribute</c> is a false negative when the
    /// attribute lives in one assembly and the reader in another.
    /// </summary>
    /// <remarks>
    /// A member is "explicitly nullable" if its own data carries <c>NullableAttribute</c> with
    /// first flag 2; or, absent its own annotation, if the tightest enclosing
    /// <c>NullableContextAttribute</c> has flag 2. A direct <c>NullableAttribute(1)</c> on the
    /// member overrides any outer context — the compiler's emit pattern for a non-nullable
    /// member inside a nullable-default scope.
    /// </remarks>
    private static bool CheckNullableAttributes(ICustomAttributeProvider attributeProvider)
    {
        var ownData = GetAttributeData(attributeProvider);
        if (ownData is not null)
        {
            foreach (var data in ownData)
            {
                if (data.AttributeType.FullName != NullableAttributeFullName) continue;
                if (TryReadFirstFlag(data, out var flag))
                {
                    // Own annotation wins outright: 2 = nullable, 1 = not-null, 0 = oblivious.
                    return flag == 2;
                }
            }
        }

        // No own annotation — walk enclosing scopes, tightest first. The first
        // NullableContextAttribute we find determines the default.
        foreach (var scopeData in EnumerateScopeAttributes(attributeProvider))
        {
            foreach (var data in scopeData)
            {
                if (data.AttributeType.FullName != NullableContextAttributeFullName) continue;
                if (TryReadFirstFlag(data, out var flag))
                {
                    return flag == 2;
                }
            }
        }

        return false;
    }

    private static IList<CustomAttributeData>? GetAttributeData(ICustomAttributeProvider provider) =>
        provider switch
        {
            MemberInfo member => member.GetCustomAttributesData(),
            ParameterInfo parameter => parameter.GetCustomAttributesData(),
            _ => null,
        };

    private static IEnumerable<IList<CustomAttributeData>> EnumerateScopeAttributes(ICustomAttributeProvider provider)
    {
        switch (provider)
        {
            case ParameterInfo parameter:
                if (parameter.Member is MethodBase method) yield return method.GetCustomAttributesData();
                if (parameter.Member?.DeclaringType is Type paramType) yield return paramType.GetCustomAttributesData();
                if (parameter.Member?.Module is Module paramModule) yield return paramModule.GetCustomAttributesData();
                break;
            case PropertyInfo property:
                if (property.DeclaringType is Type propType) yield return propType.GetCustomAttributesData();
                if (property.Module is Module propModule) yield return propModule.GetCustomAttributesData();
                break;
            case FieldInfo field:
                if (field.DeclaringType is Type fieldType) yield return fieldType.GetCustomAttributesData();
                if (field.Module is Module fieldModule) yield return fieldModule.GetCustomAttributesData();
                break;
            case MethodBase methodBase:
                if (methodBase.DeclaringType is Type methodType) yield return methodType.GetCustomAttributesData();
                if (methodBase.Module is Module methodModule) yield return methodModule.GetCustomAttributesData();
                break;
        }
    }

    private static bool TryReadFirstFlag(CustomAttributeData data, out byte flag)
    {
        // NullableAttribute / NullableContextAttribute ctor takes either (byte) or (byte[]).
        flag = 0;
        var arg = data.ConstructorArguments.FirstOrDefault();
        if (arg.Value is byte scalarFlag)
        {
            flag = scalarFlag;
            return true;
        }

        if (arg.Value is ReadOnlyCollection<CustomAttributeTypedArgument> array &&
            array.Count > 0 &&
            array[0].Value is byte arrayFlag)
        {
            flag = arrayFlag;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The key and value types of every generic <see cref="IDictionary{TKey, TValue}"/> the type
    /// implements.
    /// </summary>
    /// <example><code>
    /// foreach (var kv in typeof(Dictionary&lt;string, int&gt;).GetGenericDictionaryArgumentTypes())
    ///     Console.WriteLine($"{kv.Key} -&gt; {kv.Value}");
    /// </code></example>
    public static IEnumerable<KeyValuePair<Type, Type>> GetGenericDictionaryArgumentTypes(this Type type)
    {
        ThrowIf.ArgumentNull(type);

        foreach (var interfaceType in type.GetInterfaces())
        {
            if (interfaceType.IsGenericType &&
                ReferenceEquals(interfaceType.GetGenericTypeDefinition(), typeof(IDictionary<,>)))
            {
                var genericArguments = interfaceType.GetGenericArguments();
                yield return new KeyValuePair<Type, Type>(genericArguments[0], genericArguments[1]);
            }
        }
    }

    /// <summary>
    /// The element type of every generic <see cref="IEnumerable{T}"/> the type implements.
    /// </summary>
    /// <example><code>
    /// var elementType = typeof(List&lt;int&gt;).GetGenericEnumerableArgumentTypes().Single(); // int
    /// </code></example>
    public static IEnumerable<Type> GetGenericEnumerableArgumentTypes(this Type type)
    {
        ThrowIf.ArgumentNull(type);

        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType &&
                ReferenceEquals(@interface.GetGenericTypeDefinition(), typeof(IEnumerable<>)))
            {
                yield return @interface.GetGenericArguments()[0];
            }
        }
    }
}

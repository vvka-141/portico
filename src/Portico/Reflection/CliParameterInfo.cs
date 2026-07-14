using System.Reflection;

namespace Portico.Reflection;

internal abstract class CliParameterInfo(ParameterInfo parameter) : ParameterInfoDecorator(parameter)
{
    /// <summary>
    /// Binds this parameter from the parsed invocation. Only valid when
    /// <see cref="IsFrameworkSupplied"/> is <see langword="false"/> — callers must consult that flag
    /// first (SOL-82: the abstract contract used to be a lie enforced by an out-of-band type check).
    /// </summary>
    public abstract object? Materialize(CliInvocation invocation);

    /// <summary>
    /// True for parameters the framework fills itself rather than binding from the invocation —
    /// currently the ambient <see cref="System.Threading.CancellationToken"/>. For these,
    /// <see cref="Materialize"/> must not be called; the caller writes
    /// <see cref="FrameworkPlaceholder"/> into the argument slot and the invoker later overwrites it
    /// with the real value.
    /// </summary>
    public virtual bool IsFrameworkSupplied => false;

    /// <summary>
    /// The pre-dispatch placeholder written into a framework-supplied parameter's argument slot —
    /// a type-appropriate default (e.g. <c>default(CancellationToken)</c>) so reflection invoke
    /// never sees <see langword="null"/> for a value-type parameter.
    /// </summary>
    public virtual object? FrameworkPlaceholder => null;
}
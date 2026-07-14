namespace Portico;

/// <summary>
/// Abstract base for every parsed option shape — flag, scalar, collection, and their keyed
/// variants. The framework returns a list of these off <see cref="CliInvocation.Options"/>;
/// downstream code discriminates by concrete type or by the <see cref="ICliOptionCapture"/>
/// family of interfaces.
/// </summary>
public abstract record CliOptionCapture
{
    protected internal CliOptionCapture(string name)
    {
        Name = name;
    }

    /// <summary>The option's leading token (e.g. <c>--verbose</c>, <c>-v</c>, <c>--config</c>).</summary>
    public string Name { get; }
}

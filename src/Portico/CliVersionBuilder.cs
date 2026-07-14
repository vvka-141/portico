using System;
using System.Collections.Generic;

namespace Portico;

/// <summary>
/// Configures the <c>--version</c> response — the printed text and the triggers that produce
/// it. Obtained via <see cref="ICliApplicationBuilder.WithVersion(Action{CliVersionBuilder})"/>.
/// Default triggers are <c>--version</c> and <c>-V</c>; override with <see cref="Triggers"/> to
/// add a subcommand form (<c>myapp version</c>) or alternatives (<c>-v</c>, <c>ver</c>, etc.).
/// </summary>
public sealed class CliVersionBuilder
{
    internal Func<string>? TextFactory { get; private set; }
    internal IReadOnlyList<string>? CustomTriggers { get; private set; }

    /// <summary>
    /// Sets the version text printed when a trigger matches. Equivalent to
    /// <see cref="ICliApplicationBuilder.WithVersion(string)"/>.
    /// </summary>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .AddCommands(new MyTool())
    ///     .WithVersion(v =&gt; v.Text("myapp 2.0.0")));
    /// </code></example>
    public CliVersionBuilder Text(string text)
    {
        ThrowIf.ArgumentNull(text);
        TextFactory = () => text;
        return this;
    }

    /// <summary>
    /// Sets a lazy factory invoked once per version request. Useful when the version string is
    /// computed (e.g. includes build metadata read from a file or an assembly attribute).
    /// </summary>
    /// <example><code>
    /// builder.WithVersion(v =&gt; v.Text(() =&gt;
    ///     $"myapp {Assembly.GetEntryAssembly()!.GetName().Version}"));
    /// </code></example>
    public CliVersionBuilder Text(Func<string> factory)
    {
        ThrowIf.ArgumentNull(factory);
        TextFactory = factory;
        return this;
    }

    /// <summary>
    /// Replaces the default version triggers. Triggers that start with <c>-</c> match an option
    /// name (<c>--version</c>, <c>-V</c>); triggers without a leading dash match a bare first
    /// segment (<c>version</c>, <c>ver</c>) — enabling the <c>myapp version</c> subcommand shape.
    /// </summary>
    /// <example><code>
    /// builder.WithVersion(v =&gt; v.Text("myapp 2.0.0").Triggers("--version", "-v", "version"));
    /// </code></example>
    public CliVersionBuilder Triggers(params string[] triggers)
    {
        ThrowIf.ArgumentNull(triggers);
        if (triggers.Length == 0)
        {
            throw new ArgumentException("At least one trigger is required.", nameof(triggers));
        }
        CustomTriggers = triggers;
        return this;
    }
}

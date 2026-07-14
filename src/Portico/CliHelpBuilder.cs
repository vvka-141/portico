using System;
using System.Collections.Generic;

namespace Portico;

/// <summary>
/// Configures how the framework detects a help request. Obtained via
/// <see cref="ICliApplicationBuilder.WithHelp(Action{CliHelpBuilder})"/>. Help is on by default
/// with triggers <c>--help</c>, <c>help</c>, <c>-h</c>, <c>-?</c>, <c>?</c>; use this builder
/// to override them. Call <see cref="ICliApplicationBuilder.SuppressHelp"/> to turn help off
/// entirely (e.g. for a headless / daemon CLI).
/// </summary>
public sealed class CliHelpBuilder
{
    internal IReadOnlyList<string>? CustomTriggers { get; private set; }

    /// <summary>
    /// Replaces the default help triggers. Each trigger that starts with <c>-</c> matches an
    /// option name (<c>--help</c>, <c>-h</c>, <c>-?</c>); each trigger without a leading dash
    /// matches a bare first segment (<c>help</c>, <c>?</c>, <c>man</c>).
    /// </summary>
    /// <param name="triggers">The replacement triggers. At least one is required.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <example><code>
    /// CliApplication.Create(cfg =&gt; cfg
    ///     .AddCommands(new MyTool())
    ///     .WithHelp(help =&gt; help.Triggers("--help", "-h", "help")));
    /// </code></example>
    public CliHelpBuilder Triggers(params string[] triggers)
    {
        ThrowIf.ArgumentNull(triggers);
        if (triggers.Length == 0)
        {
            throw new ArgumentException("At least one trigger is required. To disable help entirely, call SuppressHelp().", nameof(triggers));
        }
        CustomTriggers = triggers;
        return this;
    }
}

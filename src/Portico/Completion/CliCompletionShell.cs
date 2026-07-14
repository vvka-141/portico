namespace Portico.Completion;

/// <summary>Shells for which <see cref="CliCompletion"/> can emit a completion script.</summary>
public enum CliCompletionShell
{
    /// <summary>Bash (<c>complete -F _fn name</c>).</summary>
    Bash,

    /// <summary>Zsh (<c>#compdef</c> file, <c>_describe</c>-based).</summary>
    Zsh,

    /// <summary>PowerShell (<c>Register-ArgumentCompleter</c>).</summary>
    PowerShell,
}

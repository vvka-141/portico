using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Portico.Completion;

/// <summary>
/// Emits shell-completion scripts for Portico CLIs. Writes a self-contained script to the supplied
/// <see cref="TextWriter"/> — usually invoked by a hidden <c>completion</c> subcommand in the host
/// CLI so users can pipe it into their shell's rc file.
/// </summary>
/// <remarks>
/// <para>
/// Scope is verb-level completion: the emitted scripts propose the next registered route segment
/// based on the words typed so far. Option-level completion (<c>--foo</c> flags) is not included
/// in this iteration.
/// </para>
/// <para>
/// A route signature is truncated at its first argument placeholder (<c>{argName}</c>) — the shell
/// cannot autocomplete an argument value, so completion stops where one is expected. A route such as
/// <c>db {env} migrate</c> completes only as far as <c>db</c>: deleting the interior placeholder and
/// rejoining the literals would propose <c>db migrate</c>, a command the CLI does not have.
/// </para>
/// </remarks>
public static partial class CliCompletion
{
    [System.Text.RegularExpressions.GeneratedRegex(@"\s*\{[^}]+\}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PlaceholderRegex();

    /// <summary>
    /// Writes a self-contained completion script for <paramref name="shell"/> to
    /// <paramref name="output"/>. Most callers want <see cref="CliApplication.EmitCompletion"/>
    /// instead, which supplies the route signatures from the application itself; this overload is for
    /// emitting a script for routes you already have in hand.
    /// </summary>
    /// <param name="shell">The target shell (bash or zsh).</param>
    /// <param name="executableName">The name the script completes for, as typed at the prompt.</param>
    /// <param name="routeSignatures">
    /// The route signatures to complete. Each is truncated at its first argument placeholder
    /// (<c>{name}</c>) — a shell cannot autocomplete a value, and completing past one would propose
    /// segment sequences the application does not route.
    /// </param>
    /// <param name="output">Where the script is written.</param>
    /// <example><code>
    /// // Wire it up as a hidden command, then: mytool completion bash &gt; /etc/bash_completion.d/mytool
    /// CliCompletion.Emit(CliCompletionShell.Bash, "mytool", app.GetRouteSignatures(), Console.Out);
    /// </code></example>
    public static void Emit(
        CliCompletionShell shell,
        string executableName,
        IEnumerable<string> routeSignatures,
        TextWriter output)
    {
        ThrowIf.ArgumentNullOrWhiteSpace(executableName);
        ThrowIf.ArgumentNull(routeSignatures);
        ThrowIf.ArgumentNull(output);

        var literals = routeSignatures
            .Select(TruncateAtFirstPlaceholder)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        switch (shell)
        {
            case CliCompletionShell.Bash: EmitBash(executableName, literals, output); break;
            case CliCompletionShell.Zsh: EmitZsh(executableName, literals, output); break;
            case CliCompletionShell.PowerShell: EmitPowerShell(executableName, literals, output); break;
            default: throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unsupported shell.");
        }
    }

    private static void EmitBash(string executableName, string[] literals, TextWriter output)
    {
        var fn = "_" + SanitizeIdentifier(executableName) + "_complete";
        output.WriteLine("# bash completion for " + executableName);
        output.WriteLine("# source this script (or drop it in /etc/bash_completion.d) to enable tab completion.");
        output.WriteLine(fn + "() {");
        output.WriteLine("    local cur=\"${COMP_WORDS[COMP_CWORD]}\"");
        output.WriteLine("    local prefix=\"\"");
        output.WriteLine("    local i");
        output.WriteLine("    for ((i=1; i<COMP_CWORD; i++)); do");
        output.WriteLine("        prefix+=\"${COMP_WORDS[i]} \"");
        output.WriteLine("    done");
        output.WriteLine("    prefix=\"${prefix% }\"");
        output.WriteLine("    local candidates=()");
        output.WriteLine("    local route");
        output.WriteLine("    while IFS= read -r route; do");
        output.WriteLine("        if [[ -z \"$prefix\" ]]; then");
        output.WriteLine("            candidates+=(\"${route%% *}\")");
        // "$route" != "$prefix": a truncated route is a proper prefix of its siblings (`db` beside
        // `db backup`), and without this guard it would propose itself back as `db db`.
        output.WriteLine("        elif [[ \"$route \" == \"$prefix \"* && \"$route\" != \"$prefix\" ]]; then");
        output.WriteLine("            local rest=\"${route#$prefix }\"");
        output.WriteLine("            candidates+=(\"${rest%% *}\")");
        output.WriteLine("        fi");
        output.WriteLine("    done <<'__PORTICO_ROUTES__'");
        foreach (var literal in literals)
        {
            output.WriteLine(literal);
        }
        output.WriteLine("__PORTICO_ROUTES__");
        output.WriteLine("    COMPREPLY=( $(compgen -W \"${candidates[*]}\" -- \"$cur\") )");
        output.WriteLine("}");
        output.WriteLine($"complete -F {fn} {executableName}");
    }

    private static void EmitZsh(string executableName, string[] literals, TextWriter output)
    {
        var fn = "_" + SanitizeIdentifier(executableName);
        output.WriteLine("#compdef " + executableName);
        output.WriteLine("# zsh completion for " + executableName);
        output.WriteLine(fn + "() {");
        output.WriteLine("    local prefix=\"${words[2,CURRENT-1]}\"");
        output.WriteLine("    local -a routes");
        output.WriteLine("    routes=(");
        foreach (var literal in literals)
        {
            output.WriteLine("        \"" + literal.Replace("\"", "\\\"") + "\"");
        }
        output.WriteLine("    )");
        output.WriteLine("    local -a candidates");
        output.WriteLine("    local route");
        output.WriteLine("    for route in \"${routes[@]}\"; do");
        output.WriteLine("        if [[ -z \"$prefix\" ]]; then");
        output.WriteLine("            candidates+=(\"${route%% *}\")");
        output.WriteLine("        elif [[ \"$route \" == \"$prefix \"* && \"$route\" != \"$prefix\" ]]; then");
        output.WriteLine("            local rest=\"${route#$prefix }\"");
        output.WriteLine("            candidates+=(\"${rest%% *}\")");
        output.WriteLine("        fi");
        output.WriteLine("    done");
        output.WriteLine("    compadd -- \"${candidates[@]}\"");
        output.WriteLine("}");
        output.WriteLine($"compdef {fn} {executableName}");
    }

    private static void EmitPowerShell(string executableName, string[] literals, TextWriter output)
    {
        output.WriteLine("# PowerShell completion for " + executableName);
        output.WriteLine("Register-ArgumentCompleter -Native -CommandName " + executableName + " -ScriptBlock {");
        output.WriteLine("    param($wordToComplete, $commandAst, $cursorPosition)");
        output.WriteLine("    $routes = @(");
        for (int i = 0; i < literals.Length; i++)
        {
            var comma = i < literals.Length - 1 ? "," : "";
            output.WriteLine("        '" + literals[i].Replace("'", "''") + "'" + comma);
        }
        output.WriteLine("    )");
        output.WriteLine("    $elements = $commandAst.CommandElements");
        output.WriteLine("    $prefix = if ($elements.Count -gt 1) {");
        output.WriteLine("        ($elements[1..($elements.Count - 2)] | ForEach-Object { $_.ToString() }) -join ' '");
        output.WriteLine("    } else { '' }");
        output.WriteLine("    $candidates = @()");
        output.WriteLine("    foreach ($route in $routes) {");
        output.WriteLine("        if ([string]::IsNullOrEmpty($prefix)) {");
        output.WriteLine("            $candidates += ($route -split ' ')[0]");
        output.WriteLine("        } elseif ($route.StartsWith($prefix + ' ') -or $route -eq $prefix) {");
        output.WriteLine("            $rest = $route.Substring($prefix.Length).TrimStart()");
        output.WriteLine("            if ($rest) { $candidates += ($rest -split ' ')[0] }");
        output.WriteLine("        }");
        output.WriteLine("    }");
        output.WriteLine("    $candidates | Sort-Object -Unique | Where-Object { $_.StartsWith($wordToComplete) } |");
        output.WriteLine("        ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }");
        output.WriteLine("}");
    }

    /// <summary>
    /// Cuts a route signature back to the literal prefix that precedes its first argument placeholder.
    /// Deleting placeholders and rejoining what is left would weld the literals on either side of an
    /// interior argument together (<c>db {env} migrate</c> → <c>db migrate</c>) and manufacture a route
    /// the application cannot dispatch.
    /// </summary>
    private static string TruncateAtFirstPlaceholder(string signature)
    {
        var placeholder = PlaceholderRegex().Match(signature);
        return (placeholder.Success ? signature[..placeholder.Index] : signature).Trim();
    }

    private static string SanitizeIdentifier(string name)
    {
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        return new string(chars);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Portico.Reflection;

/// <summary>
/// Renders the two help surfaces for a <see cref="CliRouteModel"/> — the compact per-route
/// summary (general help) and the rich per-command block (<c>app cmd --help</c>). Extracted from
/// <see cref="CliMethodInfo"/> (SOL-78); the two renderers share the signature / arguments /
/// options layout so their output stays byte-aligned.
/// </summary>
internal static class CliHelpRenderer
{
    /// <summary>
    /// Renders top-level help: a <c>Commands:</c> listing, and nothing else. This is the summary half
    /// of the two-level model every CLI a user has met — git, docker, dotnet, kubectl — implements.
    /// Detail belongs to <c>app &lt;command&gt; --help</c> (<see cref="RenderCommandHelp"/>); dumping
    /// every command's full option list here is what POR-31 fixed.
    /// </summary>
    /// <remarks>
    /// A single-command CLI gets that command's full help instead. A <c>Commands:</c> list of one,
    /// followed by "run 'app &lt;command&gt; --help' for more information", is a menu with one item —
    /// it makes the user type a second command to learn what the tool they already invoked does.
    /// </remarks>
    public static string RenderOverview(IReadOnlyList<CliRouteModel> models, string executableName)
    {
        if (models.Count == 1)
        {
            return RenderCommandHelp(models[0], executableName);
        }

        var sb = new StringBuilder();
        sb.Append("Usage: ").Append(executableName).AppendLine(" <command> [options]");

        if (models.Count == 0)
        {
            // No routes registered. Say so, rather than printing an empty Commands: header.
            sb.AppendLine();
            sb.AppendLine("This application declares no commands.");
            return sb.ToString();
        }

        sb.AppendLine();
        sb.AppendLine("Commands:");

        var rows = models
            .Select(m => (Signature: CommandSignature(m), Description: SummaryDescription(m)))
            .ToList();
        var colWidth = Math.Max(rows.Max(r => r.Signature.Length) + 2, 20);
        foreach (var row in rows)
        {
            AppendRow(sb, row.Signature, row.Description, colWidth);
        }

        sb.AppendLine();
        sb.Append("Run '").Append(executableName).AppendLine(" <command> --help' for more information on a command.");

        return sb.ToString();
    }

    // The route as the user types it: literals verbatim, argument slots as <NAME> / [NAME]. No
    // options — the whole point of the summary is that options live one level down.
    private static string CommandSignature(CliRouteModel model)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < model.Segments.Length; ++i)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(SignatureToken(model.Segments[i], i, model));
        }
        return sb.ToString();
    }

    // CliRouteModel.Description falls back to the method name when no [Description] is declared.
    // Echoing "MigrateAsync" back at the user is noise, not documentation — leave the column blank.
    private static string SummaryDescription(CliRouteModel model) =>
        string.Equals(model.Description, model.Name, StringComparison.Ordinal)
            ? string.Empty
            : model.Description;

    /// <summary>
    /// Renders the rich per-command help block shown in response to <c>app cmd --help</c>.
    /// Layout: Usage line / description / Arguments / Options / Examples — each section is elided
    /// when empty. <paramref name="executableName"/> prefixes the Usage line and every example so
    /// users can paste them verbatim.
    /// </summary>
    public static string RenderCommandHelp(CliRouteModel model, string executableName)
    {
        var sb = new StringBuilder();

        // Usage line
        sb.Append("Usage: ").Append(executableName);
        for (var i = 0; i < model.Segments.Length; ++i)
        {
            sb.Append(' ');
            sb.Append(SignatureToken(model.Segments[i], i, model));
        }
        var optionInfos = model.Options;
        if (optionInfos.Length > 0) sb.Append(" [options]");
        sb.AppendLine();

        AppendDescription(sb, model);
        AppendArguments(sb, model);
        AppendOptions(sb, optionInfos);

        // Examples. The attribute text is authored against the contract, which cannot know the root
        // route it was later mounted under — so the mount prefix is prepended here, or the printed
        // example would exit 2 when pasted (POR-39).
        var examples = model.Examples;
        if (examples.Length > 0)
        {
            var mount = model.MountPrefix.IsDefaultOrEmpty
                ? string.Empty
                : string.Join(' ', model.MountPrefix) + " ";

            sb.AppendLine();
            sb.AppendLine("Examples:");
            foreach (var ex in examples)
            {
                sb.Append("  ").Append(executableName).Append(' ').Append(mount).AppendLine(ex.Example);
                if (!string.IsNullOrWhiteSpace(ex.Description))
                {
                    sb.Append("      ").AppendLine(ex.Description);
                }
            }
        }

        return sb.ToString();
    }

    // <NAME> is required, [NAME] is optional — the convention git/docker/dotnet all use.
    private static string SignatureToken(CliRouteSegment seg, int position, CliRouteModel model) => seg switch
    {
        CliLiteralSegment literal => literal.Text,
        CliArgumentSegment arg => IsOptionalAt(model, position)
            ? $"[{arg.Argument.Name.ToUpperInvariant()}]"
            : $"<{arg.Argument.Name.ToUpperInvariant()}>",
        _ => "?"
    };

    private static bool IsOptionalAt(CliRouteModel model, int position) =>
        model.Parameters
            .OfType<CliArgumentParameterInfo>()
            .Any(p => p.CliRoutePosition == position && p.IsOptionalArgument);

    // Description (skip when the reflection-derived default is just the method name).
    private static void AppendDescription(StringBuilder sb, CliRouteModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.Description) && !string.Equals(model.Description, model.Name, StringComparison.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine(model.Description);
        }
    }

    // Arguments. Signature uses <NAME> (placeholder); the table lists the bare NAME —
    // matches typical git/docker conventions.
    private static void AppendArguments(StringBuilder sb, CliRouteModel model)
    {
        var args = model.Parameters.OfType<CliArgumentParameterInfo>().ToList();
        if (args.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("Arguments:");
        var colWidth = Math.Max(args.Max(a => a.CliArgumentName.Length) + 2, 18);
        foreach (var arg in args)
        {
            AppendRow(sb, arg.CliArgumentName.ToUpperInvariant(), arg.Description, colWidth);
        }
    }

    /// <summary>
    /// One "  label      description" row. The label is padded to the column, but a row with no
    /// description is NOT — padding it would emit trailing whitespace, which shows up in diffs, in
    /// `cat -A`, and in every test that compares help output (POR-31).
    /// </summary>
    private static void AppendRow(StringBuilder sb, string label, string? description, int colWidth)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            sb.Append("  ").AppendLine(label);
            return;
        }

        sb.Append("  ").Append(label.PadRight(colWidth)).AppendLine(description);
    }

    private static void AppendOptions(StringBuilder sb, ImmutableArray<ICliOptionMemberInfo> optionInfos)
    {
        if (optionInfos.Length == 0) return;

        sb.AppendLine();
        sb.AppendLine("Options:");
        var rows = optionInfos.Select(o => new
        {
            Label = FormatOptionAliasesLabel(o.Aliases, o.PipeSeparatedAliases),
            Desc = FormatOptionDescription(o),
        }).ToList();
        var colWidth = Math.Max(rows.Max(r => r.Label.Length) + 2, 20);
        foreach (var row in rows)
        {
            AppendRow(sb, row.Label, row.Desc, colWidth);
        }
    }

    private static string FormatOptionAliasesLabel(ImmutableArray<string> aliases, string fallback)
    {
        if (aliases.IsDefaultOrEmpty || aliases.Length == 0)
        {
            return fallback;
        }
        return CliOptionAttribute.FormatAliasList(aliases);
    }

    private static string FormatOptionDescription(ICliOptionMemberInfo option)
    {
        var desc = option.Description ?? string.Empty;
        if (option.IsOptional && option.DefaultValue is not null && option.DefaultValue is not CliFlag)
        {
            if (!desc.Contains("default:", StringComparison.OrdinalIgnoreCase))
            {
                var rendered = option.IsSensitive ? "***" : $"{option.DefaultValue}";
                desc = Append(desc, $"(default: {rendered})");
            }
        }

        // The variable's NAME, never its value. For a containerized service, --help is often the
        // only surface an operator has — they did not write the tool and have no checkout — so an
        // option fed by APP_PASSWORD whose help says nothing is an option they cannot operate.
        //
        // Reading the value here is the leak that stopped the rest of the ecosystem shipping this
        // (dotnet/command-line-api#1191, open since 2021). It stays a leak regardless of Sensitive:
        // a variable nobody marked sensitive can still hold something its author did not anticipate.
        // Note there is deliberately no IsSensitive branch below — there is no value to redact,
        // which is the point, and CliHelpEnvironmentVariable_Should pins that a set variable's value
        // never appears in the rendered help.
        if (!string.IsNullOrWhiteSpace(option.EnvironmentVariable) &&
            !desc.Contains("env:", StringComparison.OrdinalIgnoreCase))
        {
            desc = Append(desc, $"(env: {option.EnvironmentVariable})");
        }

        return desc.Trim();

        static string Append(string description, string suffix) =>
            string.IsNullOrWhiteSpace(description) ? suffix : $"{description}  {suffix}";
    }
}

using System;
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
    /// Renders the compact per-route summary shown in general help (app invoked with no args or
    /// bare <c>--help</c>). One block per route: Signature / Description / Arguments / Options.
    /// Skips the Usage line and Examples block (those live in per-command help).
    /// </summary>
    public static string RenderGeneralHelp(CliRouteModel model)
    {
        var sb = new StringBuilder();

        // Signature: literals verbatim, required argument slots as <NAME>, optional ones as [NAME].
        for (var i = 0; i < model.Segments.Length; ++i)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(SignatureToken(model.Segments[i], i, model));
        }
        var optionInfos = model.Options;
        if (optionInfos.Length > 0) sb.Append(" [options]");
        sb.AppendLine();

        AppendDescription(sb, model);
        AppendArguments(sb, model);
        AppendOptions(sb, optionInfos);

        return sb.ToString();
    }

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
            sb.Append("  ")
                .Append(arg.CliArgumentName.ToUpperInvariant().PadRight(colWidth))
                .AppendLine(arg.Description);
        }
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
            sb.Append("  ")
                .Append(row.Label.PadRight(colWidth))
                .AppendLine(row.Desc);
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
            // Respect a user-supplied "(default: X)" fragment rather than appending a second
            // one — common case when a consumer documented the default in prose.
            if (!desc.Contains("default:", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = $"  (default: {option.DefaultValue})";
                desc = !string.IsNullOrWhiteSpace(desc) ? desc + suffix : suffix.TrimStart();
            }
        }
        return desc.Trim();
    }
}

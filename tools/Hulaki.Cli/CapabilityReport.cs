using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Hulaki.Cli;

/// <summary>
/// Renders manifests as text or as the Markdown of <c>docs/capabilities.md</c> (ADR-0006). Lines end
/// in <c>\n</c> on every OS, so the generated file is the same on Windows and Linux.
/// </summary>
internal static class CapabilityReport
{
    public static string Text(IReadOnlyList<CapabilityManifest> manifests)
    {
        var builder = new StringBuilder();
        foreach (var manifest in manifests)
        {
            builder.Append(manifest.Platform).Append('\n')
                .Append("  text limit: ").Append(Limit(manifest)).Append('\n')
                .Append("  attachments: ").Append(manifest.MaxAttachments.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var declaration in manifest.Declarations)
            {
                builder.Append("  ").Append(declaration.Capability).Append(": ").Append(Words(declaration.Availability));
                if (declaration.Notes is not null)
                {
                    builder.Append(" (").Append(declaration.Notes).Append(')');
                }

                builder.Append('\n');
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    public static string Markdown(IReadOnlyList<CapabilityManifest> manifests)
    {
        var builder = new StringBuilder()
            .Append("# Capabilities\n\n")
            .Append("What each Hulaki provider supports, generated from its capability manifest by\n")
            .Append("`hulaki capabilities --markdown`. Do not edit it by hand: CI fails when this file and the\n")
            .Append("command disagree. A capability a provider does not declare reads as not implemented.\n\n");

        builder.Append("| | ").AppendJoin(" | ", manifests.Select(m => m.Platform)).Append(" |\n")
            .Append("| --- |").Append(string.Concat(Enumerable.Repeat(" --- |", manifests.Count))).Append('\n')
            .Append("| Text limit | ").AppendJoin(" | ", manifests.Select(Limit)).Append(" |\n")
            .Append("| Attachments | ").AppendJoin(" | ", manifests.Select(m => m.MaxAttachments.ToString(CultureInfo.InvariantCulture))).Append(" |\n");
        foreach (var capability in System.Enum.GetValues<Capability>())
        {
            builder.Append("| ").Append(capability).Append(" | ")
                .AppendJoin(" | ", manifests.Select(m => Words(m.Get(capability))))
                .Append(" |\n");
        }

        foreach (var manifest in manifests)
        {
            var notes = manifest.Declarations.Where(d => d.Notes is not null).ToArray();
            if (notes.Length == 0)
            {
                continue;
            }

            builder.Append("\n## ").Append(manifest.Platform).Append("\n\n")
                .Append("| Capability | Availability | Note |\n| --- | --- | --- |\n");
            foreach (var declaration in notes)
            {
                builder.Append("| ").Append(declaration.Capability).Append(" | ").Append(Words(declaration.Availability))
                    .Append(" | ").Append(declaration.Notes!.Replace("|", "\\|", System.StringComparison.Ordinal)).Append(" |\n");
            }
        }

        return builder.ToString();
    }

    private static string Limit(CapabilityManifest manifest) =>
        string.Create(CultureInfo.InvariantCulture, $"{manifest.TextLimit.Max:N0} {manifest.TextLimit.Counter.Unit}");

    // "UnsupportedByPlatform" reads as "unsupported by platform".
    private static string Words(Availability availability)
    {
        var name = availability.ToString();
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}

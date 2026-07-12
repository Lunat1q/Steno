using System.Text;
using Steno.Core.Session;

namespace Steno.Core.Export;

/// <summary>Human-readable transcript. Consecutive lines from one speaker are grouped.</summary>
public sealed class MarkdownTranscriptExporter : ITranscriptExporter
{
    public string Name => "Markdown";

    public string Extension => ".md";

    public string Render(IReadOnlyList<TranscriptEntry> entries, DateTimeOffset startedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Call transcript — {startedAt.LocalDateTime:yyyy-MM-dd HH:mm}");
        builder.AppendLine();

        string? lastSpeaker = null;

        foreach (var entry in entries.Where(e => e.IsFinal))
        {
            if (entry.Speaker != lastSpeaker)
            {
                builder.AppendLine();
                builder.AppendLine($"**{entry.Speaker}**");
                lastSpeaker = entry.Speaker;
            }

            builder.AppendLine($"- `{entry.Start:hh\\:mm\\:ss}` {entry.Text}");

            if (!string.IsNullOrWhiteSpace(entry.Translation))
                builder.AppendLine($"  - _{entry.Translation}_");
        }

        return builder.ToString();
    }
}

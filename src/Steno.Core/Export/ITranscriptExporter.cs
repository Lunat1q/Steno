using Steno.Core.Session;

namespace Steno.Core.Export;

public interface ITranscriptExporter
{
    /// <summary>Display name, e.g. "Markdown".</summary>
    string Name { get; }

    /// <summary>File extension, including the dot.</summary>
    string Extension { get; }

    string Render(IReadOnlyList<TranscriptEntry> entries, DateTimeOffset startedAt);
}

public static class TranscriptExporterExtensions
{
    public static async Task ExportAsync(
        this ITranscriptExporter exporter,
        string path,
        IReadOnlyList<TranscriptEntry> entries,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken = default) =>
        await File.WriteAllTextAsync(
            path,
            exporter.Render(entries, startedAt),
            System.Text.Encoding.UTF8,
            cancellationToken).ConfigureAwait(false);
}

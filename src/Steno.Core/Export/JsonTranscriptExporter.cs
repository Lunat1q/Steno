using System.Text.Json;
using System.Text.Json.Serialization;
using Steno.Core.Session;

namespace Steno.Core.Export;

/// <summary>Machine-readable transcript: timings, speaker, confidence. For downstream tooling.</summary>
public sealed class JsonTranscriptExporter : ITranscriptExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep Cyrillic readable
    };

    public string Name => "JSON";

    public string Extension => ".json";

    public string Render(IReadOnlyList<TranscriptEntry> entries, DateTimeOffset startedAt) =>
        JsonSerializer.Serialize(
            new
            {
                startedAt,
                entries = entries
                    .Where(e => e.IsFinal)
                    .Select(e => new
                    {
                        e.Speaker,
                        channel = e.Channel,
                        startSeconds = e.Start.TotalSeconds,
                        endSeconds = e.End.TotalSeconds,
                        e.Text,
                        e.Translation,
                        e.Confidence
                    })
            },
            Options);
}

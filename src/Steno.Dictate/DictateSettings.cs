using System.Text.Json;

namespace Steno.Dictate;

/// <summary>
/// What was picked last time. Its own file, next to Steno's: the two apps keep their models in the
/// same place but their choices are not the same choices, and one writing the other's file would
/// silently drop whatever it did not know about.
/// </summary>
public sealed record DictateSettings
{
    public string? MicrophoneId { get; init; }
    public string? ModelName { get; init; }
    public string? LanguageCode { get; init; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Steno",
        "dictate.json");

    public static DictateSettings Load()
    {
        try
        {
            return File.Exists(Path)
                ? JsonSerializer.Deserialize<DictateSettings>(File.ReadAllText(Path), Options) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt preferences file costs one re-pick. Throwing here costs the whole app.
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is an annoyance; crashing mid-dictation is not.
        }
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Steno.App.Composition;

/// <summary>
/// What the user picked last time. Not app config — there is nothing here a developer sets;
/// it is purely "stop asking me the same question every launch".
/// </summary>
public sealed record UserSettings
{
    public string? QualityName { get; init; }
    public string? LanguageCode { get; init; }
    public string? MicrophoneId { get; init; }
    public string? SpeakerId { get; init; }
    public bool? TranslateToEnglish { get; init; }
    public bool? ShowLiveDraft { get; init; }
    public bool? SuppressEcho { get; init; }
}

public interface IUserSettingsStore
{
    UserSettings Load();

    void Save(UserSettings settings);
}

/// <summary>Stores them next to the models, in %LOCALAPPDATA%/Steno.</summary>
public sealed class JsonUserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<JsonUserSettingsStore> _logger;

    public JsonUserSettingsStore(ILogger<JsonUserSettingsStore> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Steno",
            "settings.json");
    }

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new UserSettings();

            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path), Options)
                   ?? new UserSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable preferences file must never stop the app from starting.
            // Falling back to defaults costs the user one re-pick; throwing costs them the app.
            _logger.LogWarning(ex, "Could not read {Path}; falling back to defaults", _path);
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a preference is an annoyance; crashing mid-call because a disk was full
            // is not. Swallow it.
            _logger.LogWarning(ex, "Could not save settings to {Path}", _path);
        }
    }
}

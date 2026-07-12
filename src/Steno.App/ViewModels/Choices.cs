using Steno.Core.Transcription;

namespace Steno.App.ViewModels;

/// <summary>
/// "large-v3-turbo" means nothing to someone about to take a call. People choose along one
/// axis — how good vs how heavy — so that is the axis we offer. The whisper.cpp model name
/// stays an implementation detail.
/// </summary>
public sealed record QualityChoice(string Name, string Detail, WhisperModel Model, string DownloadSize)
{
    public static IReadOnlyList<QualityChoice> All { get; } =
    [
        new("Fast", "Runs on any laptop. Misses the odd word.", WhisperModel.Small, "~470 MB"),
        new("Balanced", "Recommended. Accurate, keeps up in real time.", WhisperModel.LargeV3Turbo, "~1.5 GB"),
        new("Best", "Most accurate. Needs a strong CPU or a GPU.", WhisperModel.LargeV3, "~3 GB")
    ];

    public static QualityChoice Default => All[1];

    public override string ToString() => Name;
}

/// <param name="Code">ISO-639-1, or "auto" to let whisper.cpp detect it.</param>
public sealed record LanguageChoice(string Name, string Code)
{
    public static IReadOnlyList<LanguageChoice> All { get; } =
    [
        new("Russian", "ru"),
        new("English", "en"),
        new("Ukrainian", "uk"),
        new("German", "de"),
        new("Spanish", "es"),
        new("Detect automatically", "auto")
    ];

    public static LanguageChoice Default => All[0];

    public override string ToString() => Name;
}

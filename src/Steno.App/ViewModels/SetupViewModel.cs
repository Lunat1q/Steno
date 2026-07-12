using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
using Steno.Core.Segmentation;
using Steno.Core.Session;
using Steno.Core.Transcription;

namespace Steno.App.ViewModels;

/// <summary>
/// Everything the user picks before a call. Separate from <see cref="MainViewModel"/> so the
/// setup screen owns its own state and the session view model stays about the session.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly IAudioDeviceProvider _devices;
    private readonly IWhisperModelProvider _models;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    private AudioDevice? _microphone;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    private AudioDevice? _speaker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadNotice))]
    [NotifyPropertyChangedFor(nameof(NeedsDownload))]
    private QualityChoice _quality = QualityChoice.Default;

    [ObservableProperty] private LanguageChoice _language = LanguageChoice.Default;
    [ObservableProperty] private bool _translateToEnglish;
    [ObservableProperty] private bool _suppressEcho = true;

    /// <summary>
    /// On by default: this is what makes the app feel live (text within ~600 ms instead of
    /// after the sentence ends). It is only affordable on a GPU — on CPU the pipeline simply
    /// drops the drafts it cannot keep up with, so leaving it on costs nothing there either.
    /// </summary>
    [ObservableProperty] private bool _showLiveDraft = true;

    public SetupViewModel(IAudioDeviceProvider devices, IWhisperModelProvider models)
    {
        _devices = devices;
        _models = models;
        RefreshDevices();
    }

    public ObservableCollection<AudioDevice> Microphones { get; } = [];

    public ObservableCollection<AudioDevice> Speakers { get; } = [];

    public IReadOnlyList<QualityChoice> Qualities => QualityChoice.All;

    public IReadOnlyList<LanguageChoice> Languages => LanguageChoice.All;

    /// <summary>One channel is enough to be useful (dictation); two is the point of the app.</summary>
    public bool IsReady => Microphone is not null || Speaker is not null;

    public bool NeedsDownload => !_models.IsDownloaded(Quality.Model);

    /// <summary>A 1.5 GB download that starts when you press Start must be announced before you press Start.</summary>
    public string? DownloadNotice => NeedsDownload
        ? $"First run downloads the {Quality.Name.ToLowerInvariant()} model ({Quality.DownloadSize}). Once only."
        : null;

    [RelayCommand]
    public void RefreshDevices()
    {
        var microphoneId = Microphone?.Id;
        var speakerId = Speaker?.Id;

        Microphones.Clear();
        Speakers.Clear();

        foreach (var device in _devices.GetDevices(AudioDeviceKind.Capture))
            Microphones.Add(device);

        foreach (var device in _devices.GetDevices(AudioDeviceKind.Render))
            Speakers.Add(device);

        // Keep the user's pick across a rescan; fall back to the system default (listed first).
        Microphone = Microphones.FirstOrDefault(d => d.Id == microphoneId) ?? Microphones.FirstOrDefault();
        Speaker = Speakers.FirstOrDefault(d => d.Id == speakerId) ?? Speakers.FirstOrDefault();
    }

    public void NotifyModelDownloaded()
    {
        OnPropertyChanged(nameof(NeedsDownload));
        OnPropertyChanged(nameof(DownloadNotice));
    }

    public SessionOptions ToSessionOptions() => new()
    {
        MicrophoneDevice = Microphone,
        LoopbackDevice = Speaker,
        SuppressCrossTalk = SuppressEcho,
        Transcription = new TranscriptionOptions
        {
            Model = Quality.Model,
            Language = Language.Code,
            Translation = TranslateToEnglish ? TranslationMode.ToEnglish : TranslationMode.Off,
            EmitPartials = ShowLiveDraft
        },
        Segmentation = new SegmentationOptions { EmitPartials = ShowLiveDraft }
    };
}

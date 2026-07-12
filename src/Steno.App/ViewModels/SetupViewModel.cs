using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Steno.App.Composition;
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
    private readonly IUserSettingsStore _settings;

    /// <summary>Suppresses the save-on-change while we are applying what we just loaded.</summary>
    private bool _restoring;

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

    /// <summary>Keep the audio, so the call can be re-transcribed later — with a better model, say.</summary>
    [ObservableProperty] private bool _recordAudio;

    /// <summary>
    /// On by default: this is what makes the app feel live (text within ~600 ms instead of
    /// after the sentence ends). It is only affordable on a GPU — on CPU the pipeline simply
    /// drops the drafts it cannot keep up with, so leaving it on costs nothing there either.
    /// </summary>
    [ObservableProperty] private bool _showLiveDraft = true;

    public SetupViewModel(
        IAudioDeviceProvider devices,
        IWhisperModelProvider models,
        IUserSettingsStore settings)
    {
        _devices = devices;
        _models = models;
        _settings = settings;

        // Nothing may be written during construction: RefreshDevices() raises property changes,
        // and saving those defaults would overwrite the very file we are about to read.
        _restoring = true;
        try
        {
            var saved = settings.Load();
            RefreshDevices();
            Restore(saved);
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>
    /// Puts back what the user chose last time. Anything that no longer exists — an unplugged
    /// headset, a quality that has since been renamed — falls back to the default rather than
    /// leaving the screen in a state the user cannot act on.
    /// </summary>
    private void Restore(UserSettings saved)
    {
        Quality = QualityChoice.All.FirstOrDefault(q => q.Name == saved.QualityName) ?? Quality;
        Language = LanguageChoice.All.FirstOrDefault(l => l.Code == saved.LanguageCode) ?? Language;

        Microphone = Microphones.FirstOrDefault(d => d.Id == saved.MicrophoneId) ?? Microphone;
        Speaker = Speakers.FirstOrDefault(d => d.Id == saved.SpeakerId) ?? Speaker;

        TranslateToEnglish = saved.TranslateToEnglish ?? TranslateToEnglish;
        ShowLiveDraft = saved.ShowLiveDraft ?? ShowLiveDraft;
        SuppressEcho = saved.SuppressEcho ?? SuppressEcho;
        RecordAudio = saved.RecordAudio ?? RecordAudio;
    }

    /// <summary>Every choice is saved the moment it is made — there is no Apply button to forget.</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_restoring)
            return;

        _settings.Save(new UserSettings
        {
            QualityName = Quality.Name,
            LanguageCode = Language.Code,
            MicrophoneId = Microphone?.Id,
            SpeakerId = Speaker?.Id,
            TranslateToEnglish = TranslateToEnglish,
            ShowLiveDraft = ShowLiveDraft,
            SuppressEcho = SuppressEcho,
            RecordAudio = RecordAudio
        });
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
        RecordAudio = RecordAudio,
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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Steno.App.Composition;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
using Steno.Core.Segmentation;
using Steno.Core.Session;
using Steno.Core.Transcription;
using Steno.Core.Whisper;

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

    /// <summary>CPU or GPU. Automatic until the user, or the speed test, says otherwise.</summary>
    [ObservableProperty] private ProcessingChoice _processing = ProcessingChoice.Default;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BenchmarkCommand))]
    private bool _isBenchmarking;

    /// <summary>Progress, then the verdict, of the speed test. Null until it is run.</summary>
    [ObservableProperty] private string? _benchmarkStatus;

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
        Processing = ProcessingChoice.All.FirstOrDefault(p => p.Name == saved.ProcessingName) ?? Processing;

        Microphone = Microphones.FirstOrDefault(d => d.Id == saved.MicrophoneId) ?? Microphone;
        Speaker = Speakers.FirstOrDefault(d => d.Id == saved.SpeakerId) ?? Speaker;

        TranslateToEnglish = saved.TranslateToEnglish ?? TranslateToEnglish;
        ShowLiveDraft = saved.ShowLiveDraft ?? ShowLiveDraft;
        SuppressEcho = saved.SuppressEcho ?? SuppressEcho;
        RecordAudio = saved.RecordAudio ?? RecordAudio;
    }

    /// <summary>The properties worth a disk write. Everything else here is transient screen state.</summary>
    private static readonly HashSet<string> Persisted =
    [
        nameof(Quality), nameof(Language), nameof(Microphone), nameof(Speaker), nameof(Processing),
        nameof(TranslateToEnglish), nameof(ShowLiveDraft), nameof(SuppressEcho), nameof(RecordAudio)
    ];

    /// <summary>Every choice is saved the moment it is made — there is no Apply button to forget.</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_restoring || !Persisted.Contains(e.PropertyName ?? string.Empty))
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
            RecordAudio = RecordAudio,
            ProcessingName = Processing.Name
        });
    }

    public ObservableCollection<AudioDevice> Microphones { get; } = [];

    public ObservableCollection<AudioDevice> Speakers { get; } = [];

    public IReadOnlyList<QualityChoice> Qualities => QualityChoice.All;

    public IReadOnlyList<LanguageChoice> Languages => LanguageChoice.All;

    public IReadOnlyList<ProcessingChoice> Processings => ProcessingChoice.All;

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

    /// <summary>
    /// Runs the chosen model once on the CPU and once on the GPU and switches to whichever won.
    ///
    /// The whole point is that the answer is not knowable in advance: a discrete GPU wins by a
    /// factor of tens, a cheap integrated one can lose outright. Takes a minute or two on a slow
    /// machine — which is the machine that needs the answer.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBenchmark))]
    private async Task BenchmarkAsync(CancellationToken cancellationToken)
    {
        IsBenchmarking = true;
        BenchmarkStatus = NeedsDownload ? "Downloading the speech model…" : "Loading the model…";

        try
        {
            var path = await _models.GetModelPathAsync(
                Quality.Model,
                new Progress<double>(value => BenchmarkStatus = $"Downloading the speech model… {value:P0}"),
                cancellationToken);

            NotifyModelDownloaded();

            var report = await WhisperBenchmark.RunAsync(
                path,
                Quality.Model,
                Language.Code,
                new Progress<string>(message => BenchmarkStatus = message),
                cancellationToken);

            // The user asked which to use; answering and then making them set it themselves is
            // half an answer. They can still override it — it is the same dropdown.
            Processing = ProcessingChoice.For(report.Recommended);

            // The model, though, is only *proposed*. Switching it for them can mean a 3 GB download
            // they never asked for, and quality is a taste as much as a speed — their call.
            BenchmarkStatus = report.Advice is { } advice
                ? $"{report.Summary} {Advise(advice)}"
                : report.Summary;
        }
        catch (Exception ex)
        {
            BenchmarkStatus = $"Speed test failed: {ex.Message}";
        }
        finally
        {
            IsBenchmarking = false;
        }
    }

    private bool CanBenchmark() => !IsBenchmarking;

    /// <summary>
    /// Turns the measured backend into advice about the model — which is the choice the user is
    /// actually able to get wrong. The numbers are extrapolated from the model that was measured,
    /// so they are given as "about", and the model itself is left on whatever they picked.
    /// </summary>
    private string Advise(ModelAdvice advice)
    {
        var recommended = QualityChoice.All.First(choice => choice.Model == advice.Model);
        var seconds = $"about {advice.Estimate.TotalSeconds:0.0} s a sentence";

        // No model keeps up here. Recommending the least-bad one and calling it live would be a
        // lie the user discovers 30 seconds into a call; recording is the honest way to use this
        // machine (ADR 0020).
        if (!advice.KeepsUpLive)
            return $"Nothing keeps up live on this machine — {recommended.Name} is the closest, at " +
                   $"{seconds}. For a transcript you can trust, tick “Save the call audio” and " +
                   "transcribe the file afterwards.";

        var current = QualityChoice.All.ToList().IndexOf(Quality);
        var target = QualityChoice.All.ToList().IndexOf(recommended);

        if (target == current)
            return $"{recommended.Name} is the right model here — {seconds}.";

        return target > current
            ? $"{recommended.Name} would keep up too ({seconds}) if you want the accuracy — it is a " +
              $"{recommended.DownloadSize} download."
            : $"{recommended.Name} is the one that keeps up here ({seconds}). {Quality.Name} is too " +
              "heavy for this machine.";
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
            EmitPartials = ShowLiveDraft,
            Backend = Processing.Backend
        },
        Segmentation = new SegmentationOptions { EmitPartials = ShowLiveDraft }
    };
}

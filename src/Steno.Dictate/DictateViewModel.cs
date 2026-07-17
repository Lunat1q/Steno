using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
using Steno.Core.Dictation;
using Steno.Core.Platform.Windows;
using Steno.Core.Segmentation;
using Steno.Core.Session;
using Steno.Core.Transcription;
using Steno.Core.Whisper;

namespace Steno.Dictate;

/// <summary>
/// Voice typing: pick a microphone, a model and a language, press Start, then click into any app's
/// text box. Words appear as they are spoken and are corrected in place as whisper changes its mind
/// — the drafts drive real keystrokes through <see cref="IncrementalTypist"/> (ADR 0025).
/// </summary>
public sealed partial class DictateViewModel : ObservableObject
{
    private readonly IWhisperModelProvider _models;
    private readonly ITranscriptionSession _session;
    private readonly IncrementalTypist _typist = new();

    /// <summary>Entries arrive on the pipeline's worker thread; typing must not interleave.</summary>
    private readonly object _typing = new();

    /// <summary>The window we have been typing into, so a change of focus abandons the correction.</summary>
    private IntPtr _target;

    /// <summary>Suppresses the save-on-change while the saved values are being applied.</summary>
    private bool _restoring;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private AudioDevice? _microphone;

    [ObservableProperty] private ModelChoice _model = ModelChoice.Default;
    [ObservableProperty] private LanguageChoice _language = LanguageChoice.Default;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText), nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isListening;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _status = "Pick a microphone and press Start.";

    public DictateViewModel(
        IAudioDeviceProvider devices,
        IWhisperModelProvider models,
        ITranscriptionSession session,
        WhisperTranscriberFactory transcriberFactory)
    {
        _models = models;
        _session = session;

        // Nothing may be saved during construction: filling the device list raises property
        // changes, and saving those defaults would overwrite the file we are about to read.
        _restoring = true;
        try
        {
            foreach (var device in devices.GetDevices(AudioDeviceKind.Capture))
                Microphones.Add(device);

            var saved = DictateSettings.Load();
            Microphone = Microphones.FirstOrDefault(d => d.Id == saved.MicrophoneId)
                         ?? Microphones.FirstOrDefault();
            Model = ModelChoice.All.FirstOrDefault(m => m.Name == saved.ModelName) ?? Model;
            Language = LanguageChoice.All.FirstOrDefault(l => l.Code == saved.LanguageCode) ?? Language;
        }
        finally
        {
            _restoring = false;
        }

        transcriberFactory.DownloadProgress = new Progress<double>(
            v => Status = $"Downloading the speech model… {v:P0}");

        _session.EntryAdded += OnEntry;
        _session.EntryUpdated += OnEntry;
        _session.StateChanged += s => OnUi(() => OnStateChanged(s));
        _session.Faulted += ex => OnUi(() =>
        {
            Status = ex.Message;
            IsBusy = false;
        });
    }

    /// <summary>Every choice is saved the moment it is made — there is no Apply button to forget.</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_restoring || e.PropertyName is not (nameof(Microphone) or nameof(Model) or nameof(Language)))
            return;

        new DictateSettings
        {
            MicrophoneId = Microphone?.Id,
            ModelName = Model.Name,
            LanguageCode = Language.Code
        }.Save();
    }

    public ObservableCollection<AudioDevice> Microphones { get; } = [];

    public IReadOnlyList<ModelChoice> Models => ModelChoice.All;

    public IReadOnlyList<LanguageChoice> Languages => LanguageChoice.All;

    public bool IsIdle => !IsListening;

    public string ButtonText => IsListening ? "Stop" : "Start";

    /// <summary>
    /// A draft — or the final — of an utterance. Both partial and final entries land here, sharing
    /// the utterance's id, which is what lets the typist correct in place rather than retype.
    /// </summary>
    private void OnEntry(TranscriptEntry entry)
    {
        if (entry.Channel != SpeakerChannel.Local)
            return;

        lock (_typing)
        {
            // If focus moved, the characters we were tracking belong to another window now.
            // Forget them rather than backspace over text nobody dictated.
            var foreground = TextInjector.ForegroundWindow();
            if (foreground != _target)
            {
                _typist.Reset();
                _target = foreground;
            }

            TextInjector.Apply(_typist.Next(entry.Id, entry.Text, entry.IsFinal));
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        if (IsListening)
        {
            await _session.StopAsync();
            return;
        }

        IsBusy = true;
        Status = _models.IsDownloaded(Model.Model) ? "Warming up…" : "Downloading the speech model…";

        lock (_typing)
        {
            _typist.Reset();
            _target = IntPtr.Zero;
        }

        try
        {
            await _session.StartAsync(new SessionOptions
            {
                MicrophoneDevice = Microphone,
                LoopbackDevice = null,
                RecordAudio = false,
                Transcription = new TranscriptionOptions
                {
                    Model = Model.Model,
                    Language = Language.Code,
                    // Drafts are the feature here: they are what makes words appear while you are
                    // still talking instead of a sentence at a time.
                    EmitPartials = true
                },
                Segmentation = new SegmentationOptions { EmitPartials = true }
            });
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanToggle() => IsListening || (Microphone is not null && !IsBusy);

    private void OnStateChanged(SessionState state)
    {
        IsListening = state is SessionState.Running or SessionState.Paused;
        Status = state switch
        {
            SessionState.Running => "Listening — click into any text box and speak.",
            SessionState.Stopping => "Stopping…",
            SessionState.Idle => "Stopped.",
            _ => Status
        };
    }

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}

/// <summary>The one-axis model choice, same mapping Steno uses: how good vs how heavy.</summary>
public sealed record ModelChoice(string Name, WhisperModel Model)
{
    public static IReadOnlyList<ModelChoice> All { get; } =
    [
        new("Fast", WhisperModel.Small),
        new("Balanced", WhisperModel.LargeV3Turbo),
        new("Best", WhisperModel.LargeV3)
    ];

    public static ModelChoice Default => All[1];

    public override string ToString() => Name;
}

/// <param name="Code">ISO-639-1, or "auto" to let whisper.cpp detect it.</param>
public sealed record LanguageChoice(string Name, string Code)
{
    public static IReadOnlyList<LanguageChoice> All { get; } =
    [
        new("English", "en"),
        new("Russian", "ru"),
        new("German", "de"),
        new("Spanish", "es"),
        new("Detect automatically", "auto")
    ];

    public static LanguageChoice Default => All[0];

    public override string ToString() => Name;
}

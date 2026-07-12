using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Steno.Core.Abstractions;
using Steno.Core.Export;
using Steno.Core.Session;

namespace Steno.App.ViewModels;

/// <summary>
/// The session, as the screen sees it: setup → listening → transcript. Owns no audio and no
/// inference; it drives <see cref="ITranscriptionSession"/> and marshals its events onto the
/// UI thread.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ITranscriptionSession _session;
    private readonly ITranscriptionBackend _backend;
    private readonly ILogger<MainViewModel> _logger;
    private readonly DispatcherTimer _clock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetupVisible), nameof(IsTranscriptVisible), nameof(IsReviewing))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(TogglePauseCommand))]
    private bool _isSessionActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PauseLabel))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetupVisible), nameof(IsReviewing))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isBusy;

    /// <summary>
    /// The transcript exists only in memory until it is exported. Stopping, starting a new call
    /// or closing the window would destroy it, so each of those has to ask first.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReviewing))]
    private bool _hasUnsavedTranscript;

    /// <summary>Set when a destructive action is pending and we are waiting for a yes/no.</summary>
    [ObservableProperty] private bool _isConfirmingDiscard;

    [ObservableProperty] private string _statusLine = string.Empty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private double _downloadPercent;
    [ObservableProperty] private double _yourLevel;
    [ObservableProperty] private double _theirLevel;
    [ObservableProperty] private string _elapsed = "00:00";
    [ObservableProperty] private string _backendLabel = string.Empty;

    /// <summary>True once we know the model fell back to CPU, where "live" is not achievable.</summary>
    [ObservableProperty] private bool _isSlowBackend;

    public MainViewModel(
        SetupViewModel setup,
        ITranscriptionSession session,
        ITranscriptionBackend backend,
        IEnumerable<ITranscriptExporter> exporters,
        ILogger<MainViewModel> logger)
    {
        Setup = setup;
        _session = session;
        _backend = backend;
        _logger = logger;
        Exporters = exporters.ToList();

        // Progress<T> captures the creating context — built on the UI thread, so download
        // progress lands there without a Dispatcher hop.
        ModelDownloadProgress = new Progress<double>(value => DownloadPercent = value * 100);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => UpdateElapsed();

        _session.EntryAdded += entry => OnUiThread(() => AddEntry(entry));
        _session.EntryUpdated += entry => OnUiThread(() => UpdateEntry(entry));
        _session.StateChanged += state => OnUiThread(() => OnStateChanged(state));
        _session.LevelChanged += (channel, level) => OnUiThread(() => SetLevel(channel, level));
        _session.Faulted += ex => OnUiThread(() => ErrorMessage = Explain(ex));
    }

    public SetupViewModel Setup { get; }

    public IReadOnlyList<ITranscriptExporter> Exporters { get; }

    public IProgress<double> ModelDownloadProgress { get; }

    public ObservableCollection<TranscriptEntryViewModel> Entries { get; } = [];

    public bool IsSetupVisible => !IsSessionActive && !IsBusy && !HasEntries;

    /// <summary>Shown while the call runs *and* after it stops — stopping must never wipe the screen.</summary>
    public bool IsTranscriptVisible => IsSessionActive || HasEntries;

    /// <summary>Call over, transcript still here. The moment to offer a save, not to throw it away.</summary>
    public bool IsReviewing => !IsSessionActive && !IsBusy && HasEntries;

    public bool HasEntries => Entries.Count > 0;

    public string PauseLabel => IsPaused ? "Resume" : "Pause";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        // A new call overwrites the last transcript. Ask before destroying it.
        if (HasUnsavedTranscript)
        {
            IsConfirmingDiscard = true;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        IsConfirmingDiscard = false;
        StatusLine = Setup.NeedsDownload ? "Downloading the speech model…" : "Warming up…";

        try
        {
            ClearTranscript();

            await _session.StartAsync(Setup.ToSessionOptions());

            // The backend is only known once the native library has loaded a model.
            BackendLabel = _backend.Backend;
            IsSlowBackend = !_backend.IsGpu;

            Setup.NotifyModelDownloaded();
            _clock.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start failed");
            ErrorMessage = Explain(ex);
            StatusLine = string.Empty;
        }
        finally
        {
            IsBusy = false;
            DownloadPercent = 0;
        }
    }

    private bool CanStart() => !IsSessionActive && !IsBusy && Setup.IsReady;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        StatusLine = "Finishing the last few words…";

        try
        {
            await _session.StopAsync();
        }
        finally
        {
            _clock.Stop();
            IsPaused = false;
            YourLevel = 0;
            TheirLevel = 0;
        }
    }

    private bool CanStop() => IsSessionActive;

    /// <summary>
    /// A break in the call, or a stretch nobody wants on the record. The devices stay open, so
    /// resuming is instant — and nothing said in between is ever sent to whisper.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private void TogglePause()
    {
        IsPaused = !IsPaused;
        _session.SetPaused(IsPaused);
    }

    /// <summary>Called by the view once the user has picked a file. The only thing that clears the unsaved flag.</summary>
    public async Task ExportAsync(string path, ITranscriptExporter exporter)
    {
        if (_session.StartedAt is not { } startedAt)
            return;

        await exporter.ExportAsync(path, _session.Entries, startedAt);

        HasUnsavedTranscript = false;
        IsConfirmingDiscard = false;
        StatusLine = $"Saved to {Path.GetFileName(path)}";
    }

    /// <summary>The user chose to throw the transcript away after being asked. Their call.</summary>
    [RelayCommand]
    private void DiscardTranscript()
    {
        ClearTranscript();
        IsConfirmingDiscard = false;
        StatusLine = string.Empty;
    }

    [RelayCommand]
    private void CancelDiscard() => IsConfirmingDiscard = false;

    private void ClearTranscript()
    {
        Entries.Clear();
        HasUnsavedTranscript = false;
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(IsSetupVisible));
        OnPropertyChanged(nameof(IsTranscriptVisible));
        OnPropertyChanged(nameof(IsReviewing));
    }

    private void AddEntry(TranscriptEntry entry)
    {
        // Channels finish out of order (a 2 s remote line can land before a 1 s local one that
        // started earlier), so insert by start time — the transcript must read as it was spoken.
        var index = Entries.Count;
        while (index > 0 && Entries[index - 1].Start > entry.Start)
            index--;

        Entries.Insert(index, new TranscriptEntryViewModel(entry));

        // Anything final on screen is something that would be lost on exit.
        if (entry.IsFinal)
            HasUnsavedTranscript = true;

        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(IsSetupVisible));
        OnPropertyChanged(nameof(IsTranscriptVisible));
        OnPropertyChanged(nameof(IsReviewing));
    }

    private void UpdateEntry(TranscriptEntry entry)
    {
        foreach (var existing in Entries)
        {
            if (existing.Id != entry.Id)
                continue;

            existing.Apply(entry);
            return;
        }

        AddEntry(entry);
    }

    private void SetLevel(SpeakerChannel channel, float level)
    {
        if (channel == SpeakerChannel.Local)
            YourLevel = level;
        else
            TheirLevel = level;
    }

    private void OnStateChanged(SessionState state)
    {
        IsSessionActive = state is SessionState.Running or SessionState.Paused;
        IsPaused = state is SessionState.Paused;

        StatusLine = state switch
        {
            SessionState.Preparing => StatusLine,
            SessionState.Running => "Listening",
            SessionState.Paused => "Paused — not transcribing",
            SessionState.Stopping => "Finishing the last few words…",
            SessionState.Idle => HasEntries ? "Call ended" : string.Empty,
            _ => StatusLine
        };
    }

    private void UpdateElapsed()
    {
        if (_session.StartedAt is not { } startedAt)
            return;

        var elapsed = DateTimeOffset.UtcNow - startedAt;
        Elapsed = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    }

    /// <summary>
    /// Stack traces are not a UX. The three failures a user actually hits get a sentence they
    /// can act on; anything else falls back to the raw message.
    /// </summary>
    private static string Explain(Exception exception) => exception switch
    {
        InvalidOperationException => exception.Message,
        UnauthorizedAccessException =>
            "Windows blocked access to the microphone. Settings → Privacy → Microphone, then allow desktop apps.",
        HttpRequestException =>
            "Couldn't download the speech model. Check your connection and press Start again.",
        _ => exception.Message
    };

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}

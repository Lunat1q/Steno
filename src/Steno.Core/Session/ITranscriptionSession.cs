namespace Steno.Core.Session;

public enum SessionState
{
    Idle,

    /// <summary>Downloading/loading the whisper.cpp model and opening the audio endpoints.</summary>
    Preparing,

    Running,

    /// <summary>Devices still open, transcription deliberately suspended — a break in the call.</summary>
    Paused,

    Stopping,
    Faulted
}

/// <summary>
/// A live call: two audio channels in, one time-ordered transcript out.
/// Events may be raised from capture or worker threads — the UI marshals them.
/// </summary>
public interface ITranscriptionSession : IAsyncDisposable
{
    SessionState State { get; }

    DateTimeOffset? StartedAt { get; }

    /// <summary>Where the call was recorded, when recording was on. Null otherwise.</summary>
    string? RecordingPath { get; }

    /// <summary>Time-ordered across both channels.</summary>
    IReadOnlyList<TranscriptEntry> Entries { get; }

    event Action<TranscriptEntry>? EntryAdded;
    event Action<TranscriptEntry>? EntryUpdated;
    event Action<SessionState>? StateChanged;
    event Action<Exception>? Faulted;

    /// <summary>Smoothed 0..1 loudness per channel. The UI turns this into a meter, so a dead
    /// microphone or a silent loopback device is visible immediately instead of at minute five.</summary>
    event Action<SpeakerChannel, float>? LevelChanged;

    Task StartAsync(SessionOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Suspends transcription without ending the call: for a break, or a stretch of the
    /// conversation that must not be recorded. Devices stay open, so resuming is instant and
    /// the transcript's timeline stays aligned with the call.
    /// </summary>
    void SetPaused(bool paused);

    Task StopAsync();
}

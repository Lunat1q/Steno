using Steno.Core.Audio;
using Steno.Core.Segmentation;
using Steno.Core.Transcription;

namespace Steno.Core.Session;

public sealed record SessionOptions
{
    /// <summary>Microphone. Null disables the local channel.</summary>
    public AudioDevice? MicrophoneDevice { get; init; }

    /// <summary>Render endpoint to capture in loopback. Null disables the remote channel.</summary>
    public AudioDevice? LoopbackDevice { get; init; }

    public string LocalSpeakerName { get; init; } = "Me";
    public string RemoteSpeakerName { get; init; } = "Remote";

    public TranscriptionOptions Transcription { get; init; } = new();
    public SegmentationOptions Segmentation { get; init; } = new();

    /// <summary>
    /// Drop microphone utterances that are just the remote party leaking back in through
    /// loudspeakers (ADR 0006). Harmless on a headset; essential without one.
    /// </summary>
    public bool SuppressCrossTalk { get; init; } = true;

    /// <summary>
    /// Save the call as a stereo WAV (left = you, right = them) so it can be re-transcribed later
    /// with speaker attribution intact. Audio recorded while paused is never written (ADR 0020).
    /// </summary>
    public bool RecordAudio { get; init; }

    /// <summary>Where recordings go. Defaults to Documents/Steno/recordings.</summary>
    public string? RecordingDirectory { get; init; }
}

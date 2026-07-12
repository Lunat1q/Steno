using Steno.Core.Audio;

namespace Steno.Core.Abstractions;

/// <summary>
/// A live source of 16 kHz mono float32 audio. Implementations own all device-format
/// ugliness (resampling, downmix, int16→float) and never leak it upward.
/// The Windows implementations are microphone capture and render-device loopback.
/// </summary>
public interface IAudioCaptureSource : IAsyncDisposable
{
    /// <summary>Raised for every captured block, already normalised. Fired on a capture thread.</summary>
    event Action<AudioFrame>? FrameAvailable;

    /// <summary>Raised when the underlying device dies (unplugged, format changed, exclusive-mode grab).</summary>
    event Action<Exception>? Faulted;

    bool IsCapturing { get; }

    /// <param name="clockOrigin">
    /// Session start. Frame offsets are measured from here, so independent sources
    /// share one timeline and their transcripts interleave correctly.
    /// </param>
    Task StartAsync(DateTimeOffset clockOrigin, CancellationToken cancellationToken = default);

    Task StopAsync();
}

public interface IAudioDeviceProvider
{
    IReadOnlyList<AudioDevice> GetDevices(AudioDeviceKind kind);
}

/// <summary>Builds a capture source for a chosen device. The only place device ids become streams.</summary>
public interface IAudioCaptureSourceFactory
{
    IAudioCaptureSource Create(AudioDevice device);
}

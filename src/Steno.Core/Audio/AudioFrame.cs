namespace Steno.Core.Audio;

/// <summary>
/// A block of captured audio, already normalised to the only format whisper.cpp accepts:
/// 16 kHz, mono, float32 in [-1, 1].
/// </summary>
/// <param name="Samples">PCM samples. Owned by the receiver; capture sources hand off fresh arrays.</param>
/// <param name="Offset">Time of the first sample, relative to session start.</param>
public readonly record struct AudioFrame(float[] Samples, TimeSpan Offset)
{
    public TimeSpan Duration => AudioConstants.DurationOf(Samples.Length);
    public TimeSpan End => Offset + Duration;
}

public static class AudioConstants
{
    /// <summary>whisper.cpp is hard-wired to 16 kHz. Not a preference.</summary>
    public const int SampleRate = 16_000;

    public const int Channels = 1;

    /// <summary>VAD/segmentation granularity.</summary>
    public const int FrameMilliseconds = 20;

    public const int FrameSamples = SampleRate * FrameMilliseconds / 1000; // 320

    public static TimeSpan DurationOf(int sampleCount) =>
        TimeSpan.FromSeconds((double)sampleCount / SampleRate);

    public static int SamplesFor(TimeSpan duration) =>
        (int)Math.Round(duration.TotalSeconds * SampleRate);

    public static int SamplesForMs(int milliseconds) => SampleRate * milliseconds / 1000;
}

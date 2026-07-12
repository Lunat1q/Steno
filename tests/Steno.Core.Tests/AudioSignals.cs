using Steno.Core.Audio;

namespace Steno.Core.Tests;

/// <summary>Synthetic audio. A 220 Hz tone stands in for a voice: its zero-crossing rate
/// sits in the vocal band, which is what the VAD actually keys on.</summary>
internal static class AudioSignals
{
    public static float[] Tone(TimeSpan duration, float amplitude = 0.3f, float frequency = 220f)
    {
        var samples = new float[AudioConstants.SamplesFor(duration)];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = amplitude * MathF.Sin(2 * MathF.PI * frequency * i / AudioConstants.SampleRate);

        return samples;
    }

    public static float[] Silence(TimeSpan duration) =>
        new float[AudioConstants.SamplesFor(duration)];

    public static float[] Concat(params float[][] parts) =>
        parts.SelectMany(p => p).ToArray();

    /// <summary>Feeds the segmenter the way a capture device would: in 20 ms frames, on one timeline.</summary>
    public static IEnumerable<AudioFrame> ToFrames(float[] samples)
    {
        for (var offset = 0; offset + AudioConstants.FrameSamples <= samples.Length; offset += AudioConstants.FrameSamples)
        {
            yield return new AudioFrame(
                samples.AsSpan(offset, AudioConstants.FrameSamples).ToArray(),
                AudioConstants.DurationOf(offset));
        }
    }
}

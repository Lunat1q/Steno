using Steno.Core.Abstractions;

namespace Steno.Core.Segmentation;

/// <summary>
/// Energy VAD with an adaptive noise floor, plus a zero-crossing-rate veto.
///
/// RMS alone flags fan noise and line hum; ZCR alone flags fricatives and keyboard clicks.
/// Speech = energy well above the tracked noise floor AND a ZCR in the vocal band.
/// The floor adapts only while the frame is judged non-speech, so a long monologue
/// cannot drag the floor up and mute the speaker.
///
/// ponytail: no model. Adequate on a headset. Swap in Silero behind IVoiceActivityDetector
/// if the room is noisy — see ADR 0003.
/// </summary>
public sealed class EnergyVoiceActivityDetector : IVoiceActivityDetector
{
    private const float InitialNoiseFloor = 1e-4f;
    private const float FloorAttack = 0.05f;   // rise toward a louder noise floor
    private const float FloorRelease = 0.002f; // fall toward a quieter one: slow, avoids chasing speech gaps

    /// <summary>Speech must exceed the noise floor by this factor (~12 dB).</summary>
    private const float SnrFactor = 4.0f;

    /// <summary>Absolute floor. Below this it is digital silence regardless of the adaptive floor.</summary>
    private const float AbsoluteFloor = 3e-4f;

    private const float MinZeroCrossingRate = 0.005f;
    private const float MaxZeroCrossingRate = 0.30f;

    private float _noiseFloor = InitialNoiseFloor;

    public float NoiseFloor => _noiseFloor;

    public bool IsSpeech(ReadOnlySpan<float> frame)
    {
        if (frame.IsEmpty)
            return false;

        var rms = Rms(frame);
        var speech =
            rms > AbsoluteFloor &&
            rms > _noiseFloor * SnrFactor &&
            IsVocalZeroCrossingRate(frame);

        // Only adapt on non-speech, so speech never raises the bar against itself.
        if (!speech)
        {
            var rate = rms > _noiseFloor ? FloorAttack : FloorRelease;
            _noiseFloor += (rms - _noiseFloor) * rate;
            _noiseFloor = Math.Max(_noiseFloor, 1e-6f);
        }

        return speech;
    }

    public void Reset() => _noiseFloor = InitialNoiseFloor;

    public static float Rms(ReadOnlySpan<float> frame)
    {
        if (frame.IsEmpty)
            return 0f;

        double sum = 0;
        foreach (var sample in frame)
            sum += (double)sample * sample;

        return (float)Math.Sqrt(sum / frame.Length);
    }

    private static bool IsVocalZeroCrossingRate(ReadOnlySpan<float> frame)
    {
        var crossings = 0;
        for (var i = 1; i < frame.Length; i++)
        {
            if ((frame[i] >= 0f) != (frame[i - 1] >= 0f))
                crossings++;
        }

        var rate = (float)crossings / frame.Length;
        return rate is >= MinZeroCrossingRate and <= MaxZeroCrossingRate;
    }
}

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

    /// <summary>Rise toward a louder room. Deliberately slow: a fast rise is how the detector
    /// talks itself into silence.</summary>
    private const float FloorRise = 0.003f;

    /// <summary>Fall toward a quieter room. Faster than the rise — being too sensitive is
    /// recoverable, being deaf is not.</summary>
    private const float FloorFall = 0.05f;

    /// <summary>
    /// Hard ceiling on the learned floor. Speech RMS sits around 0.1–0.3, so with the 4× SNR
    /// factor this keeps the speech threshold at most 0.08 — below any normal voice. Even if
    /// every heuristic above it is wrong, the detector cannot become permanently deaf.
    /// </summary>
    private const float MaxNoiseFloor = 0.02f;

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

        // "Loud" is about energy alone. Speech also has to sound like a voice, but that second
        // test must NOT feed back into the noise floor — see below.
        var loud = rms > AbsoluteFloor && rms > _noiseFloor * SnrFactor;
        var speech = loud && IsVocalZeroCrossingRate(frame);

        // The floor learns the *room*, so it may only learn from frames that are quiet relative
        // to it. Learning from loud frames is a death spiral: a fricative ("s", "sh") is as loud
        // as a vowel but fails the zero-crossing test, so it used to count as "not speech" and
        // drag the floor up toward speech level. A few seconds of talking then put the threshold
        // above the speaker's own voice and the detector went permanently deaf — meters moving,
        // no text, forever.
        if (!loud)
        {
            var rate = rms > _noiseFloor ? FloorRise : FloorFall;
            _noiseFloor += (rms - _noiseFloor) * rate;
            _noiseFloor = Math.Clamp(_noiseFloor, 1e-6f, MaxNoiseFloor);
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

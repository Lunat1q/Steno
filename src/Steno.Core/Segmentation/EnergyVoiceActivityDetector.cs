using Steno.Core.Abstractions;

namespace Steno.Core.Segmentation;

/// <summary>
/// Energy VAD with an adaptive noise floor, plus a zero-crossing-rate veto.
///
/// RMS alone flags fan noise and line hum; ZCR alone flags fricatives and keyboard clicks.
/// Speech = energy well above the tracked noise floor AND a ZCR in the vocal band.
///
/// The floor is the 10th percentile of the last few seconds of audio: whatever the channel is
/// doing when nobody is talking. A percentile is used rather than an EMA that only learns from
/// frames the detector already judged quiet, because that scheme cannot see a background it never
/// gets below. Over film audio — dialogue on a bed of score and effects — every single frame was
/// "loud", so the floor stayed pinned at its minimum, every frame was speech, no silence ever
/// closed an utterance, and the transcript was force-cut 20 s bricks (ADR 0015).
///
/// ponytail: no model. Adequate on a headset, adequate on a film mix. Swap in Silero behind
/// IVoiceActivityDetector if music-heavy input needs to be tighter — see ADR 0003.
/// </summary>
public sealed class EnergyVoiceActivityDetector : IVoiceActivityDetector
{
    private const float InitialNoiseFloor = 1e-4f;

    /// <summary>How much history the floor is drawn from. Long enough to span the pauses between
    /// sentences — the quiet part of the window is the whole point — short enough to follow a
    /// scene change.</summary>
    private const int FloorWindowFrames = 150; // 3 s of 20 ms frames

    /// <summary>Where in that window the background lives. Speech occupies the top of the window;
    /// the bottom tenth is the room, the hiss, or the score under the dialogue.</summary>
    private const float FloorPercentile = 0.10f;

    /// <summary>
    /// Hard ceiling on the learned floor. With the 3× SNR factor this keeps the speech threshold
    /// at most 0.045 — below even a quiet talker (~0.05). Even if every heuristic above it is
    /// wrong, the detector cannot become permanently deaf.
    /// </summary>
    private const float MaxNoiseFloor = 0.015f;

    /// <summary>Speech must exceed the noise floor by this factor (~10 dB). Lower than a headset
    /// would need: dialogue mixed over music does not get 12 dB of headroom.</summary>
    private const float SnrFactor = 3.0f;

    /// <summary>Absolute floor. Below this it is digital silence regardless of the adaptive floor.</summary>
    private const float AbsoluteFloor = 3e-4f;

    private const float MinZeroCrossingRate = 0.005f;
    private const float MaxZeroCrossingRate = 0.30f;

    private readonly float[] _recent = new float[FloorWindowFrames];
    private int _recentCount;
    private int _next;

    private float _noiseFloor = InitialNoiseFloor;

    public float NoiseFloor => _noiseFloor;

    public bool IsSpeech(ReadOnlySpan<float> frame)
    {
        if (frame.IsEmpty)
            return false;

        var rms = Rms(frame);

        _recent[_next] = rms;
        _next = (_next + 1) % FloorWindowFrames;
        _recentCount = Math.Min(_recentCount + 1, FloorWindowFrames);
        _noiseFloor = EstimateNoiseFloor();

        // "Loud" is about energy alone; speech also has to sound like a voice. The ZCR veto never
        // feeds back into the floor — a fricative ("s", "sh") is as loud as a vowel but fails it,
        // and a floor that learned from those frames would climb to speech level and go deaf.
        var loud = rms > AbsoluteFloor && rms > _noiseFloor * SnrFactor;

        return loud && IsVocalZeroCrossingRate(frame);
    }

    public void Reset()
    {
        _noiseFloor = InitialNoiseFloor;
        _recentCount = 0;
        _next = 0;
    }

    private float EstimateNoiseFloor()
    {
        Span<float> window = stackalloc float[FloorWindowFrames];
        _recent.AsSpan(0, _recentCount).CopyTo(window);
        window = window[.._recentCount];
        window.Sort();

        var floor = window[(int)(window.Length * FloorPercentile)];
        return Math.Clamp(floor, 1e-6f, MaxNoiseFloor);
    }

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

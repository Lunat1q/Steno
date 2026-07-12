using Steno.Core.Audio;
using Steno.Core.Segmentation;
using Xunit;
using Xunit.Abstractions;

namespace Steno.Core.Tests;

/// <summary>
/// Regression: the app transcribed for 15–30 seconds and then went permanently deaf — meters
/// still moved, no text ever appeared again.
///
/// Real speech is not a pure tone. Fricatives ("s", "sh", "f") are loud *and* have a high
/// zero-crossing rate, so the VAD's ZCR veto classifies them as non-speech — and the noise floor
/// then adapted toward their (speech-level) energy. Every such frame raised the bar against the
/// speaker, until the threshold sat above the voice and nothing was ever speech again.
///
/// These tests use voice-like audio — tone plus sibilance — and run long enough for the spiral
/// to close.
/// </summary>
public class VadStabilityTests(ITestOutputHelper output)
{
    /// <summary>Voiced tone with a burst of sibilance, like an actual word.</summary>
    private static float[] Word(TimeSpan duration)
    {
        var samples = AudioSignals.Tone(duration, amplitude: 0.3f);
        var random = new Random(1);

        // Last fifth is a fricative: same loudness, noise-like (high zero-crossing rate).
        var fricativeStart = samples.Length * 4 / 5;
        for (var i = fricativeStart; i < samples.Length; i++)
            samples[i] = 0.3f * (float)(random.NextDouble() * 2 - 1);

        return samples;
    }

    [Fact]
    public void The_detector_still_hears_speech_after_a_minute_of_talking()
    {
        var vad = new EnergyVoiceActivityDetector();
        var segmenter = new UtteranceSegmenter(vad, new SegmentationOptions());

        var utterances = new List<Utterance>();
        segmenter.UtteranceReady += utterances.Add;

        // One minute of conversation: a word, a pause, a word, a pause…
        var turn = AudioSignals.Concat(
            Word(TimeSpan.FromSeconds(2)),
            AudioSignals.Silence(TimeSpan.FromSeconds(1)));

        var firstHalf = 0;

        for (var i = 0; i < 20; i++)
        {
            foreach (var frame in AudioSignals.ToFrames(turn))
                segmenter.Push(frame);

            if (i == 9)
            {
                firstHalf = utterances.Count;
                output.WriteLine($"after 30 s: {firstHalf} utterances, noise floor {vad.NoiseFloor:E2}");
            }
        }

        var secondHalf = utterances.Count - firstHalf;
        output.WriteLine($"after 60 s: {utterances.Count} utterances total, noise floor {vad.NoiseFloor:E2}");

        // The failure mode is not "fewer" — it is "none at all, forever".
        Assert.True(
            secondHalf >= 8,
            $"the detector went deaf: {firstHalf} utterances in the first 30 s, only {secondHalf} in the second");
    }

    [Fact]
    public void Loud_speech_never_raises_the_noise_floor_against_the_speaker()
    {
        var vad = new EnergyVoiceActivityDetector();

        var quiet = AudioSignals.Silence(TimeSpan.FromSeconds(1));
        foreach (var frame in AudioSignals.ToFrames(quiet))
            vad.IsSpeech(frame.Samples);

        var floorInSilence = vad.NoiseFloor;

        // 20 s of continuous loud, voice-like audio.
        for (var i = 0; i < 10; i++)
        {
            foreach (var frame in AudioSignals.ToFrames(Word(TimeSpan.FromSeconds(2))))
                vad.IsSpeech(frame.Samples);
        }

        output.WriteLine($"floor in silence {floorInSilence:E2} → after loud speech {vad.NoiseFloor:E2}");

        // The floor tracks the *room*, not the voice. Speech must never teach it to ignore speech.
        Assert.True(
            vad.NoiseFloor < 0.02f,
            $"noise floor climbed to {vad.NoiseFloor:E2} — the threshold is now above normal speech");
    }
}

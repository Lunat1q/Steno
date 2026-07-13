using Steno.Core.Segmentation;
using Steno.Core.Transcription;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>
/// Regression: a film played over the loopback channel produced four words in three minutes, all
/// of them shouted. The pre-whisper energy gate sat at speech level (0.004 RMS) — but loopback
/// audio arrives at whatever the volume slider and the output device leave of it. Measured through
/// a virtual endpoint at 70%, real film dialogue landed at 0.0012–0.008 RMS, so the gate deleted
/// every line that was not a scream (ADR 0023).
/// </summary>
public class TranscriptionPolicyTests
{
    /// <summary>Quiet but real: film dialogue as it actually arrives over loopback.</summary>
    private static float[] QuietSpeech() => AudioSignals.Tone(TimeSpan.FromSeconds(1), amplitude: 0.003f);

    [Fact]
    public void Speech_captured_at_a_low_playback_volume_still_reaches_whisper()
    {
        var quiet = QuietSpeech();
        var rms = EnergyVoiceActivityDetector.Rms(quiet);

        Assert.InRange(rms, 0.001f, 0.008f); // the level the real capture produced
        Assert.True(
            TranscriptionPolicy.IsWorthTranscribing(quiet),
            $"dialogue at {rms:F4} RMS was gated as silence — that is a volume slider, not a silent room");
    }

    [Fact]
    public void Digital_silence_is_still_kept_away_from_whisper()
    {
        // The gate's real job (ADR 0019): whisper answers silence with invented subtitles.
        Assert.False(TranscriptionPolicy.IsWorthTranscribing(AudioSignals.Silence(TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Quiet_audio_is_brought_up_to_the_level_whisper_expects()
    {
        var normalized = TranscriptionPolicy.Normalize(QuietSpeech());
        var rms = EnergyVoiceActivityDetector.Rms(normalized);

        // The 30× cap is reached before the target here — deliberately, since the cap is what keeps
        // a near-silent channel from being amplified into whisper's imagination. Speech-adjacent is
        // the goal, not exactly TargetRms.
        Assert.InRange(rms, 0.05f, TranscriptionPolicy.TargetRms);
    }

    [Fact]
    public void Audio_within_reach_of_the_cap_lands_on_the_target()
    {
        var normalized = TranscriptionPolicy.Normalize(AudioSignals.Tone(TimeSpan.FromSeconds(1), amplitude: 0.02f));

        Assert.Equal(
            TranscriptionPolicy.TargetRms,
            EnergyVoiceActivityDetector.Rms(normalized),
            tolerance: 0.005f);
    }

    [Fact]
    public void The_boost_is_capped_so_a_silent_channel_is_never_amplified_into_speech()
    {
        // Room tone at 30× is still room tone. Without the cap it would be whisper's cue to invent.
        var hiss = AudioSignals.Tone(TimeSpan.FromSeconds(1), amplitude: 1e-5f);
        var normalized = TranscriptionPolicy.Normalize(hiss);

        var before = EnergyVoiceActivityDetector.Rms(hiss);
        var after = EnergyVoiceActivityDetector.Rms(normalized);

        Assert.True(after <= before * TranscriptionPolicy.MaxGain * 1.01f);
        Assert.True(after < TranscriptionPolicy.MinSpeechRms, "amplified hiss must stay below the silence gate");
    }

    [Fact]
    public void Loud_audio_is_not_pushed_into_clipping()
    {
        var loud = AudioSignals.Tone(TimeSpan.FromSeconds(1), amplitude: 0.9f);
        var normalized = TranscriptionPolicy.Normalize(loud);

        Assert.All(normalized, sample => Assert.InRange(sample, -1f, 1f));
    }
}

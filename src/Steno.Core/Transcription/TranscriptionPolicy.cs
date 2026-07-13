using Steno.Core.Segmentation;

namespace Steno.Core.Transcription;

/// <summary>
/// What is worth sending to whisper, and what is worth keeping from it.
///
/// Shared by the live pipeline and the offline (recording) one, deliberately: a rule that only
/// half the app obeys is a rule that will one day let "Продолжение следует..." into a transcript
/// through the door nobody was watching (ADR 0019).
/// </summary>
public static class TranscriptionPolicy
{
    /// <summary>
    /// Below this RMS the audio is digital silence, not a sentence.
    ///
    /// Deliberately *not* set at speech level. Loopback audio arrives at whatever the volume slider
    /// and the output device's own processing leave of it: measured through a virtual endpoint at
    /// 70% volume, film dialogue landed at an RMS of 0.0012–0.008 — real, intelligible speech, an
    /// order of magnitude below the same file on disk. A gate placed at "what speech sounds like"
    /// therefore deletes half a conversation and keeps the shouting (ADR 0023).
    ///
    /// Whether this utterance is speech was already decided, relative to the channel's own noise
    /// floor, by the VAD. This gate exists only to keep whisper away from *nothing at all*, since
    /// whisper answers silence with confident, invented subtitles.
    /// </summary>
    public const float MinSpeechRms = 3e-4f;

    /// <summary>What whisper expects to be handed: ordinary speech, around −22 dBFS.</summary>
    public const float TargetRms = 0.08f;

    /// <summary>
    /// Ceiling on the boost. Room tone amplified 100× is what whisper hallucinates over, so a
    /// channel that is nearly silent stays nearly silent.
    /// </summary>
    public const float MaxGain = 30f;

    /// <summary>Gate on the audio as captured — before any gain, or amplified hiss would sail through.</summary>
    public static bool IsWorthTranscribing(ReadOnlySpan<float> samples) =>
        EnergyVoiceActivityDetector.Rms(samples) >= MinSpeechRms;

    /// <summary>
    /// Brings an utterance to the level whisper was trained on.
    ///
    /// whisper.cpp does not normalise its input, and it is measurably worse on quiet audio: the same
    /// dialogue that transcribed correctly from the file came back empty when captured 12× quieter
    /// over loopback. The capture level is not something the user should have to get right, so the
    /// fix belongs here rather than in a setup screen.
    /// </summary>
    public static float[] Normalize(ReadOnlyMemory<float> samples)
    {
        var audio = samples.ToArray();
        var rms = EnergyVoiceActivityDetector.Rms(audio);

        if (rms <= 0f)
            return audio;

        var gain = Math.Min(TargetRms / rms, MaxGain);

        for (var i = 0; i < audio.Length; i++)
            audio[i] = Math.Clamp(audio[i] * gain, -1f, 1f);

        return audio;
    }

    /// <summary>True when the result is real dialogue and belongs in the transcript.</summary>
    public static bool IsUsable(TranscriptionResult result, float noSpeechThreshold) =>
        !result.IsEmpty &&
        result.NoSpeechProbability <= noSpeechThreshold &&
        !HallucinationFilter.IsHallucination(result.Text);
}

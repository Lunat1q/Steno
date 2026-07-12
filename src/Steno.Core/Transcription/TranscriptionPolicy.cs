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
    /// Below this RMS the audio is hiss, not a sentence. Speech sits at 0.05–0.3 even from a quiet
    /// talker, so the margin is wide. Keeping silence away from whisper matters because whisper
    /// answers silence with confident, invented subtitles.
    /// </summary>
    public const float MinSpeechRms = 0.004f;

    public static bool IsWorthTranscribing(ReadOnlySpan<float> samples) =>
        EnergyVoiceActivityDetector.Rms(samples) >= MinSpeechRms;

    /// <summary>True when the result is real dialogue and belongs in the transcript.</summary>
    public static bool IsUsable(TranscriptionResult result, float noSpeechThreshold) =>
        !result.IsEmpty &&
        result.NoSpeechProbability <= noSpeechThreshold &&
        !HallucinationFilter.IsHallucination(result.Text);
}

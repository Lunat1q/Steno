namespace Steno.Core.Transcription;

/// <param name="Text">Transcribed text, trimmed. Empty when whisper found nothing worth reporting.</param>
/// <param name="Confidence">Mean token probability, 0..1.</param>
/// <param name="NoSpeechProbability">whisper's own "this was not speech" estimate, 0..1.</param>
/// <param name="DetectedLanguage">Null when the language was forced.</param>
public sealed record TranscriptionResult(
    string Text,
    float Confidence,
    float NoSpeechProbability,
    string? DetectedLanguage)
{
    public static readonly TranscriptionResult Empty = new(string.Empty, 0f, 1f, null);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

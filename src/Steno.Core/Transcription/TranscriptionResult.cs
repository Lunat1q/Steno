namespace Steno.Core.Transcription;

/// <summary>
/// One decoded token and how sure whisper.cpp was of it. This is the same per-token
/// probability whisper.cpp's own `--print-colors` mode paints with: it is the model saying
/// "this word I heard clearly; that one I guessed".
/// </summary>
/// <param name="Probability">0..1. Below ~0.6 the word is close to a guess.</param>
public readonly record struct TranscriptToken(string Text, float Probability);

/// <param name="Text">Transcribed text, trimmed. Empty when whisper found nothing worth reporting.</param>
/// <param name="Confidence">Mean token probability, 0..1.</param>
/// <param name="NoSpeechProbability">whisper's own "this was not speech" estimate, 0..1.</param>
/// <param name="DetectedLanguage">Null when the language was forced.</param>
/// <param name="Tokens">Per-token confidence, in order. Empty if the engine did not report any.</param>
public sealed record TranscriptionResult(
    string Text,
    float Confidence,
    float NoSpeechProbability,
    string? DetectedLanguage,
    IReadOnlyList<TranscriptToken> Tokens)
{
    public static readonly TranscriptionResult Empty = new(string.Empty, 0f, 1f, null, []);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

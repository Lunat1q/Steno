namespace Steno.Core.Transcription;

public enum TranslationMode
{
    /// <summary>Transcript in the spoken language only.</summary>
    Off,

    /// <summary>
    /// Second whisper pass with the `translate` task. whisper.cpp can only ever
    /// translate *to English* — see docs/decisions/0005-translation.md.
    /// </summary>
    ToEnglish
}

/// <summary>Model sizes as published by ggml-org/whisper.cpp.</summary>
public enum WhisperModel
{
    Tiny,
    Base,
    Small,
    Medium,
    LargeV3,
    /// <summary>Best accuracy/speed trade-off for live use. Default.</summary>
    LargeV3Turbo
}

public sealed record TranscriptionOptions
{
    public WhisperModel Model { get; init; } = WhisperModel.LargeV3Turbo;

    /// <summary>ISO-639-1 code, or "auto". Forced by default: auto-detect flaps on short utterances.</summary>
    public string Language { get; init; } = "ru";

    public TranslationMode Translation { get; init; } = TranslationMode.Off;

    /// <summary>0 = let whisper.cpp pick (cores - 1).</summary>
    public int Threads { get; init; }

    /// <summary>
    /// Utterances whose whisper no-speech probability exceeds this are dropped.
    /// whisper.cpp answers confident nonsense when handed breath or keyboard noise.
    /// </summary>
    public float NoSpeechThreshold { get; init; } = 0.6f;

    /// <summary>Re-transcribe the growing buffer mid-utterance. Doubles CPU; see ADR 0003.</summary>
    public bool EmitPartials { get; init; }
}

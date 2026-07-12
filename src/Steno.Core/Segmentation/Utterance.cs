using Steno.Core.Audio;

namespace Steno.Core.Segmentation;

public enum UtteranceKind
{
    /// <summary>Speech is still ongoing; this is a snapshot for a provisional transcript.</summary>
    Partial,

    /// <summary>Speech ended (silence) or was force-cut at the max length. This is the real thing.</summary>
    Final
}

/// <param name="Samples">Contiguous 16 kHz mono float32 audio, pre-roll included.</param>
public sealed record Utterance(
    Guid Id,
    UtteranceKind Kind,
    ReadOnlyMemory<float> Samples,
    TimeSpan Start)
{
    public TimeSpan Duration => AudioConstants.DurationOf(Samples.Length);
    public TimeSpan End => Start + Duration;
}

public sealed record SegmentationOptions
{
    /// <summary>
    /// Speech must persist this long before an utterance opens. Rejects clicks and keystrokes,
    /// and every millisecond of it delays the first draft — the pre-roll buffer means the audio
    /// itself is not lost, only the moment we start paying attention.
    /// </summary>
    public int SpeechOnsetMs { get; init; } = 80;

    /// <summary>
    /// Silence this long closes the utterance — the dominant term in *final* latency.
    /// 400 ms is about the floor: shorter and natural mid-sentence pauses start cutting
    /// sentences in half, which costs whisper more accuracy than the latency is worth.
    /// </summary>
    public int SilenceCloseMs { get; init; } = 400;

    /// <summary>Audio kept before the onset, so the first phoneme is not clipped.</summary>
    public int PrerollMs { get; init; } = 300;

    /// <summary>Force-cut for someone who never pauses. Bounds worst-case latency.</summary>
    public int MaxUtteranceMs { get; init; } = 20_000;

    /// <summary>Shorter than this is a cough. Dropped — whisper.cpp hallucinates on it.</summary>
    public int MinUtteranceMs { get; init; } = 250;

    /// <summary>
    /// How often a Partial is emitted while speech is ongoing — this, plus inference time, is
    /// what the user experiences as "latency". 400 ms + ~170 ms on a GPU lands comfortably
    /// under a second. On CPU inference alone is seconds, so partials are pointless there and
    /// the pipeline drops them rather than falling behind (ADR 0010).
    /// </summary>
    public int PartialIntervalMs { get; init; } = 400;

    public bool EmitPartials { get; init; }
}

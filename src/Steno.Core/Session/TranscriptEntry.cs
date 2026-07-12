using Steno.Core.Segmentation;
using Steno.Core.Transcription;

namespace Steno.Core.Session;

public enum SpeakerChannel
{
    /// <summary>Microphone. The user of this machine.</summary>
    Local,

    /// <summary>Render-device loopback. The other party (ADR 0002).</summary>
    Remote
}

/// <param name="IsFinal">False for a provisional partial, which a later final entry replaces.</param>
/// <param name="Tokens">
/// Per-word confidence, so the UI can show *which* words the model was unsure of rather than
/// only an average nobody can act on. Empty when the engine reported none.
/// </param>
public sealed record TranscriptEntry(
    Guid Id,
    SpeakerChannel Channel,
    string Speaker,
    TimeSpan Start,
    TimeSpan End,
    string Text,
    string? Translation,
    float Confidence,
    bool IsFinal,
    IReadOnlyList<TranscriptToken> Tokens)
{
    public override string ToString() => $"[{Start:hh\\:mm\\:ss}] {Speaker}: {Text}";
}

/// <summary>
/// Names the speaker behind an utterance. Today the channel *is* the answer (ADR 0002).
/// This exists so that diarizing 3+ remote participants later is a swap, not a redesign.
/// </summary>
public interface ISpeakerResolver
{
    string Resolve(SpeakerChannel channel, Utterance utterance);
}

public sealed class ChannelSpeakerResolver : ISpeakerResolver
{
    private readonly string _local;
    private readonly string _remote;

    public ChannelSpeakerResolver(string local, string remote)
    {
        _local = local;
        _remote = remote;
    }

    public string Resolve(SpeakerChannel channel, Utterance utterance) =>
        channel == SpeakerChannel.Local ? _local : _remote;
}

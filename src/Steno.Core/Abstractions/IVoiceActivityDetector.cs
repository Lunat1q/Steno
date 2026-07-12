namespace Steno.Core.Abstractions;

/// <summary>
/// Decides whether a single ~20 ms frame contains speech.
/// Stateful: implementations may adapt to the noise floor, so one instance per channel.
/// </summary>
public interface IVoiceActivityDetector
{
    /// <param name="frame">Exactly one frame of 16 kHz mono float32 samples.</param>
    bool IsSpeech(ReadOnlySpan<float> frame);

    void Reset();
}

using Steno.Core.Transcription;

namespace Steno.Core.Abstractions;

/// <summary>
/// Turns a finite buffer of 16 kHz mono float32 audio into text.
/// The implementation is whisper.cpp (ADR 0001); nothing above this interface knows that.
/// Instances are NOT thread-safe — whisper.cpp contexts aren't. One per channel.
/// </summary>
public interface ITranscriber : IAsyncDisposable
{
    /// <param name="draft">
    /// True for a mid-sentence partial: decode greedily, skip the temperature fallbacks. The
    /// text is provisional and about to be replaced, so accuracy is worth trading for latency.
    /// </param>
    Task<TranscriptionResult> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        bool draft = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same audio, whisper's `translate` task. Target is always English — a whisper.cpp
    /// constraint, not a parameter. Null when translation is disabled.
    /// </summary>
    Task<TranscriptionResult> TranslateAsync(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken = default);
}

/// <summary>Creates a transcriber per channel. Model load is expensive; implementations share the model.</summary>
public interface ITranscriberFactory : IAsyncDisposable
{
    Task<ITranscriber> CreateAsync(TranscriptionOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Which native whisper.cpp backend is in use. The single biggest factor in latency
/// (GPU ≈ 170 ms/utterance, CPU ≈ 11 s), so the UI has to be able to say it out loud.
/// </summary>
public interface ITranscriptionBackend
{
    /// <summary>Human-readable, e.g. "GPU (Vulkan)" or "CPU". Only meaningful once a model is loaded.</summary>
    string Backend { get; }

    bool IsGpu { get; }
}

/// <summary>Locates or downloads a whisper.cpp GGML model file.</summary>
public interface IWhisperModelProvider
{
    /// <param name="progress">0..1 download progress. Not reported when the model is cached.</param>
    Task<string> GetModelPathAsync(
        WhisperModel model,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    bool IsDownloaded(WhisperModel model);
}

using System.Text;
using System.Text.RegularExpressions;
using Steno.Core.Abstractions;
using Steno.Core.Transcription;
using Whisper.net;

namespace Steno.Core.Whisper;

/// <summary>
/// ITranscriber over ggml-org/whisper.cpp (via the Whisper.net P/Invoke binding, ADR 0001).
///
/// Owns one whisper.cpp processor for transcription and, only when translation is enabled,
/// a second one for the `translate` task. whisper.cpp contexts are not thread-safe, so this
/// class serialises calls with a semaphore and each channel gets its own instance.
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private readonly WhisperProcessor _transcribeProcessor;
    private readonly WhisperProcessor _draftProcessor;
    private readonly WhisperProcessor? _translateProcessor;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public WhisperTranscriber(
        WhisperProcessor transcribeProcessor,
        WhisperProcessor draftProcessor,
        WhisperProcessor? translateProcessor)
    {
        _transcribeProcessor = transcribeProcessor;
        _draftProcessor = draftProcessor;
        _translateProcessor = translateProcessor;
    }

    public Task<TranscriptionResult> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        bool draft = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(draft ? _draftProcessor : _transcribeProcessor, samples, cancellationToken);

    public Task<TranscriptionResult> TranslateAsync(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken = default) =>
        _translateProcessor is null
            ? Task.FromResult(TranscriptionResult.Empty)
            : RunAsync(_translateProcessor, samples, cancellationToken);

    private async Task<TranscriptionResult> RunAsync(
        WhisperProcessor processor,
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = new StringBuilder();
            var probabilitySum = 0f;
            var noSpeechMin = 1f;
            var segments = 0;
            string? language = null;

            await foreach (var segment in processor
                               .ProcessAsync(samples, cancellationToken)
                               .ConfigureAwait(false))
            {
                text.Append(segment.Text);
                probabilitySum += segment.Probability;
                // Any single segment claiming speech is enough; whisper reports this per segment.
                noSpeechMin = Math.Min(noSpeechMin, segment.NoSpeechProbability);
                language ??= segment.Language;
                segments++;
            }

            if (segments == 0)
                return TranscriptionResult.Empty;

            return new TranscriptionResult(
                Clean(text.ToString()),
                probabilitySum / segments,
                noSpeechMin,
                language);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// whisper.cpp annotates non-speech events inline — "[BLANK_AUDIO]", "(музыка)",
    /// "*sighs*" — and prefixes every segment with a space. None of that is dialogue.
    /// </summary>
    internal static string Clean(string text) =>
        WhitespaceRuns.Replace(NonSpeechAnnotations.Replace(text, " "), " ").Trim();

    private static readonly Regex NonSpeechAnnotations =
        new(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRuns = new(@"\s+", RegexOptions.Compiled);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _transcribeProcessor.DisposeAsync().ConfigureAwait(false);
        await _draftProcessor.DisposeAsync().ConfigureAwait(false);

        if (_translateProcessor is not null)
            await _translateProcessor.DisposeAsync().ConfigureAwait(false);

        _gate.Dispose();
    }
}

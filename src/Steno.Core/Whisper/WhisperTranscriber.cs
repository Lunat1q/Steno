using System.Text;
using System.Text.RegularExpressions;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
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

    /// <summary>
    /// Pushes a short silent buffer through every processor before the call starts.
    ///
    /// The first inference on a GPU backend is *far* slower than the rest — Vulkan compiles its
    /// compute pipelines on first use — and whichever utterance pays that cost arrives seconds
    /// late, or gets dropped as the queue backs up behind it. That was the opening seconds of
    /// every call being silently swallowed. Pay it here instead, while the UI is already showing
    /// "Warming up…" and nobody is talking yet.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        var silence = new float[AudioConstants.SampleRate]; // 1 s

        await RunAsync(_transcribeProcessor, silence, cancellationToken).ConfigureAwait(false);
        await RunAsync(_draftProcessor, silence, cancellationToken).ConfigureAwait(false);

        if (_translateProcessor is not null)
            await RunAsync(_translateProcessor, silence, cancellationToken).ConfigureAwait(false);
    }

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
            var tokens = new List<TranscriptToken>();
            var probabilitySum = 0f;
            var noSpeechMin = 1f;
            var segments = 0;
            string? language = null;

            await foreach (var segment in processor
                               .ProcessAsync(samples, cancellationToken)
                               .ConfigureAwait(false))
            {
                text.Append(segment.Text);
                CollectTokens(segment, tokens);

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
                language,
                tokens);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Per-token probabilities, as whisper.cpp's own `--print-colors` uses them. Special tokens
    /// (`&lt;|ru|&gt;`, `[_BEG_]`, timestamps) carry probabilities too but are not words, so they
    /// are dropped — colouring them would be colouring the model's internal punctuation.
    /// </summary>
    private static void CollectTokens(SegmentData segment, List<TranscriptToken> tokens)
    {
        if (segment.Tokens is null)
            return;

        foreach (var token in segment.Tokens)
        {
            var text = token?.Text;
            if (string.IsNullOrEmpty(text) || IsSpecialToken(text))
                continue;

            tokens.Add(new TranscriptToken(text, token!.Probability));
        }
    }

    private static bool IsSpecialToken(string text) =>
        (text.StartsWith("<|", StringComparison.Ordinal) && text.EndsWith("|>", StringComparison.Ordinal)) ||
        (text.StartsWith("[_", StringComparison.Ordinal) && text.EndsWith(']'));

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

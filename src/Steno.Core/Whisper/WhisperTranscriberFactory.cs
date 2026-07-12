using Microsoft.Extensions.Logging;
using Steno.Core.Abstractions;
using Steno.Core.Transcription;
using Whisper.net;

namespace Steno.Core.Whisper;

/// <summary>
/// Loads the GGML model into memory once and hands out one whisper.cpp processor set per
/// channel. The model weights are the expensive part (0.8–3 GB); processors are cheap.
/// So: one WhisperFactory (the model), N transcribers (the contexts).
/// </summary>
public sealed class WhisperTranscriberFactory : ITranscriberFactory, ITranscriptionBackend
{
    private readonly IWhisperModelProvider _modelProvider;
    private readonly ILogger<WhisperTranscriberFactory> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private WhisperFactory? _whisperFactory;
    private WhisperModel? _loadedModel;
    private bool _disposed;

    public WhisperTranscriberFactory(
        IWhisperModelProvider modelProvider,
        ILogger<WhisperTranscriberFactory> logger)
    {
        _modelProvider = modelProvider;
        _logger = logger;
    }

    /// <summary>Progress of a model download, if one is needed. Surfaced by the UI before a call starts.</summary>
    public IProgress<double>? DownloadProgress { get; set; }

    public async Task<ITranscriber> CreateAsync(
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var factory = await GetOrLoadModelAsync(options.Model, cancellationToken).ConfigureAwait(false);

        var transcribe = BuildProcessor(factory, options, translate: false);

        // Same model, cheaper decode: partials are replaced within a second, so beam search and
        // temperature fallbacks buy accuracy nobody will ever read (ADR 0010).
        var draft = BuildProcessor(factory, options, translate: false, draft: true);

        var translate = options.Translation == TranslationMode.ToEnglish
            ? BuildProcessor(factory, options, translate: true)
            : null;

        return new WhisperTranscriber(transcribe, draft, translate);
    }

    /// <summary>Which native backend whisper.cpp actually loaded. Null until the first model load.</summary>
    public string Backend => WhisperRuntime.Describe();

    public bool IsGpu => WhisperRuntime.IsGpu;

    private async Task<WhisperFactory> GetOrLoadModelAsync(WhisperModel model, CancellationToken cancellationToken)
    {
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_whisperFactory is not null && _loadedModel == model)
                return _whisperFactory;

            if (_whisperFactory is not null)
            {
                _logger.LogInformation("Switching model {Old} → {New}; unloading", _loadedModel, model);
                _whisperFactory.Dispose();
                _whisperFactory = null;
                _loadedModel = null;
            }

            var path = await _modelProvider
                .GetModelPathAsync(model, DownloadProgress, cancellationToken)
                .ConfigureAwait(false);

            // Must happen before the first load: the backend is chosen when the native lib loads.
            WhisperRuntime.Configure();

            _logger.LogInformation("Loading whisper.cpp model from {Path}", path);
            _whisperFactory = WhisperFactory.FromPath(path);
            _loadedModel = model;

            _logger.LogInformation("whisper.cpp running on {Backend}", WhisperRuntime.Describe());

            return _whisperFactory;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private static WhisperProcessor BuildProcessor(
        WhisperFactory factory,
        TranscriptionOptions options,
        bool translate,
        bool draft = false)
    {
        var builder = factory.CreateBuilder()
            .WithProbabilities()
            .WithNoSpeechThreshold(options.NoSpeechThreshold)
            // Utterances are already VAD-cut and independent. Carrying decoder context across
            // them is how whisper.cpp gets into repetition loops on live audio.
            .WithNoContext();

        // A draft is on screen for under a second before the final overwrites it. Greedy, single
        // pass, no temperature retries — cut the decode cost, keep the encoder cost (which is
        // the bulk anyway, and fixed: whisper always encodes a padded 30 s window).
        if (draft)
            builder = builder.WithGreedySamplingStrategy();

        if (options.Threads > 0)
            builder = builder.WithThreads(options.Threads);

        builder = string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase)
            ? builder.WithLanguageDetection()
            : builder.WithLanguage(options.Language);

        // whisper.cpp's translate task targets English and nothing else (ADR 0005).
        if (translate)
            builder = builder.WithTranslate();

        return builder.Build();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _whisperFactory?.Dispose();
        _loadGate.Dispose();
        return ValueTask.CompletedTask;
    }
}

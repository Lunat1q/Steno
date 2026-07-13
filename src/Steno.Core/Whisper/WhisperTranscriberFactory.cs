using System.Diagnostics;
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
    private bool _loadedOnGpu;
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

        var factory = await GetOrLoadModelAsync(options.Model, options.Backend, cancellationToken)
            .ConfigureAwait(false);

        // Building a processor allocates a native whisper context — synchronous, and not free.
        // Off the caller's thread with everything else (ADR 0021).
        var transcriber = await Task.Run(() =>
        {
            var transcribe = BuildProcessor(factory, options, translate: false);

            // Same model, cheaper decode: partials are replaced within a second, so beam search
            // and temperature fallbacks buy accuracy nobody will ever read (ADR 0010).
            var draft = BuildProcessor(factory, options, translate: false, draft: true);

            var translate = options.Translation == TranslationMode.ToEnglish
                ? BuildProcessor(factory, options, translate: true)
                : null;

            return new WhisperTranscriber(transcribe, draft, translate);
        }, cancellationToken).ConfigureAwait(false);

        // Pay the GPU's first-inference cost now, not on the caller's first sentence.
        var warmUp = Stopwatch.StartNew();
        await transcriber.WarmUpAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Warmed up whisper processors in {Elapsed} ms", warmUp.ElapsedMilliseconds);

        return transcriber;
    }

    /// <summary>Which processor whisper.cpp actually ran on. Only meaningful once a model is loaded.</summary>
    public string Backend => WhisperRuntime.Describe(_loadedOnGpu);

    public bool IsGpu => _loadedOnGpu && WhisperRuntime.GpuAvailable;

    private async Task<WhisperFactory> GetOrLoadModelAsync(
        WhisperModel model,
        ComputeBackend backend,
        CancellationToken cancellationToken)
    {
        var useGpu = WhisperRuntime.WantsGpu(backend);

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The processor is baked into the model load, so a change of backend needs a reload
            // just as much as a change of model does.
            if (_whisperFactory is not null && _loadedModel == model && _loadedOnGpu == useGpu)
                return _whisperFactory;

            if (_whisperFactory is not null)
            {
                _logger.LogInformation("Reloading: model {Old} → {New}, gpu {WasGpu} → {UseGpu}",
                    _loadedModel, model, _loadedOnGpu, useGpu);
                _whisperFactory.Dispose();
                _whisperFactory = null;
                _loadedModel = null;
            }

            var path = await _modelProvider
                .GetModelPathAsync(model, DownloadProgress, cancellationToken)
                .ConfigureAwait(false);

            // Must happen before the first load: the native library is chosen when it loads.
            WhisperRuntime.Configure();

            _logger.LogInformation("Loading whisper.cpp model from {Path} (gpu: {UseGpu})", path, useGpu);

            // Task.Run, because FromPath is a synchronous native call that reads 1.5–3 GB of
            // weights. Everything above it completes synchronously once the model is cached, so
            // without this the load runs on whatever thread pressed Start — the UI thread — and
            // the window freezes for a second or two (ADR 0021).
            _whisperFactory = await Task
                .Run(() => WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = useGpu }),
                    cancellationToken)
                .ConfigureAwait(false);

            _loadedModel = model;
            _loadedOnGpu = useGpu;

            // "GPU" means the user asked for one on purpose. Falling back to a CPU that takes
            // 11 s per sentence is not a fallback, it is a different app — say so instead.
            if (backend == ComputeBackend.Gpu && !WhisperRuntime.GpuAvailable)
            {
                _whisperFactory.Dispose();
                _whisperFactory = null;
                _loadedModel = null;
                _loadedOnGpu = false;

                throw new InvalidOperationException(
                    "No GPU backend could be loaded on this machine. Choose Automatic or CPU under " +
                    "More options.");
            }

            _logger.LogInformation("whisper.cpp running on {Backend}", WhisperRuntime.Describe(_loadedOnGpu));

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

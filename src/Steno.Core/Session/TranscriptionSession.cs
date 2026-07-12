using Microsoft.Extensions.Logging;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
using Steno.Core.Segmentation;

namespace Steno.Core.Session;

/// <summary>
/// Wires the channels of a call and merges their transcripts onto one timeline.
///
/// The two pipelines transcribe independently and finish out of order (a 2 s remote
/// utterance can land before a 1 s mic utterance that started earlier), so entries are
/// inserted by start time rather than appended.
/// </summary>
public sealed class TranscriptionSession : ITranscriptionSession
{
    private readonly IAudioCaptureSourceFactory _captureFactory;
    private readonly ITranscriberFactory _transcriberFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TranscriptionSession> _logger;

    private readonly List<ChannelPipeline> _pipelines = [];
    private readonly List<TranscriptEntry> _entries = [];
    private readonly Dictionary<Guid, TranscriptEntry> _byId = [];
    private readonly object _sync = new();

    private CrossTalkGate? _crossTalkGate;
    private SessionState _state = SessionState.Idle;

    public TranscriptionSession(
        IAudioCaptureSourceFactory captureFactory,
        ITranscriberFactory transcriberFactory,
        ILoggerFactory loggerFactory)
    {
        _captureFactory = captureFactory;
        _transcriberFactory = transcriberFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TranscriptionSession>();
    }

    public SessionState State => _state;

    public DateTimeOffset? StartedAt { get; private set; }

    public IReadOnlyList<TranscriptEntry> Entries
    {
        get
        {
            lock (_sync)
                return _entries.ToList();
        }
    }

    public event Action<TranscriptEntry>? EntryAdded;
    public event Action<TranscriptEntry>? EntryUpdated;
    public event Action<SessionState>? StateChanged;
    public event Action<Exception>? Faulted;
    public event Action<SpeakerChannel, float>? LevelChanged;

    public async Task StartAsync(SessionOptions options, CancellationToken cancellationToken = default)
    {
        if (_state is SessionState.Running or SessionState.Preparing)
            return;

        if (options.MicrophoneDevice is null && options.LoopbackDevice is null)
            throw new InvalidOperationException("Select at least one device: a microphone, a speaker, or both.");

        SetState(SessionState.Preparing);

        try
        {
            lock (_sync)
            {
                _entries.Clear();
                _byId.Clear();
            }

            _crossTalkGate = options.SuppressCrossTalk &&
                             options.MicrophoneDevice is not null &&
                             options.LoopbackDevice is not null
                ? new CrossTalkGate()
                : null;

            var speakerResolver = new ChannelSpeakerResolver(options.LocalSpeakerName, options.RemoteSpeakerName);

            if (options.MicrophoneDevice is not null)
                _pipelines.Add(await BuildPipelineAsync(
                    SpeakerChannel.Local, options.MicrophoneDevice, options, speakerResolver, cancellationToken)
                    .ConfigureAwait(false));

            if (options.LoopbackDevice is not null)
                _pipelines.Add(await BuildPipelineAsync(
                    SpeakerChannel.Remote, options.LoopbackDevice, options, speakerResolver, cancellationToken)
                    .ConfigureAwait(false));

            // One clock origin for every channel: this is what lets the transcripts interleave.
            var clockOrigin = DateTimeOffset.UtcNow;
            StartedAt = clockOrigin;

            foreach (var pipeline in _pipelines)
                await pipeline.StartAsync(clockOrigin, cancellationToken).ConfigureAwait(false);

            SetState(SessionState.Running);
            _logger.LogInformation("Session started with {Count} channel(s)", _pipelines.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start session");
            await TearDownAsync().ConfigureAwait(false);
            SetState(SessionState.Faulted);
            Faulted?.Invoke(ex);
            throw;
        }
    }

    public void SetPaused(bool paused)
    {
        if (_state is not (SessionState.Running or SessionState.Paused))
            return;

        foreach (var pipeline in _pipelines)
            pipeline.IsPaused = paused;

        SetState(paused ? SessionState.Paused : SessionState.Running);
    }

    public async Task StopAsync()
    {
        if (_state is not (SessionState.Running or SessionState.Paused or SessionState.Faulted))
            return;

        SetState(SessionState.Stopping);

        foreach (var pipeline in _pipelines)
        {
            try
            {
                await pipeline.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping pipeline");
            }
        }

        await TearDownAsync().ConfigureAwait(false);
        SetState(SessionState.Idle);
    }

    private async Task<ChannelPipeline> BuildPipelineAsync(
        SpeakerChannel channel,
        AudioDevice device,
        SessionOptions options,
        ISpeakerResolver speakerResolver,
        CancellationToken cancellationToken)
    {
        var segmentation = options.Segmentation with { EmitPartials = options.Transcription.EmitPartials };

        var pipeline = new ChannelPipeline(
            channel,
            _captureFactory.Create(device),
            new UtteranceSegmenter(new EnergyVoiceActivityDetector(), segmentation),
            await _transcriberFactory.CreateAsync(options.Transcription, cancellationToken).ConfigureAwait(false),
            speakerResolver,
            options.Transcription,
            _crossTalkGate,
            _loggerFactory.CreateLogger($"Pipeline.{channel}"));

        pipeline.EntryProduced += OnEntryProduced;
        pipeline.EntryUpdated += OnEntryUpdated;
        pipeline.Faulted += OnPipelineFaulted;
        pipeline.LevelChanged += level => LevelChanged?.Invoke(channel, level);

        return pipeline;
    }

    private void OnEntryProduced(TranscriptEntry entry)
    {
        bool isNew;

        lock (_sync)
        {
            // Partials share the utterance's id, so a final overwrites the partial in place
            // instead of duplicating the line.
            isNew = !_byId.ContainsKey(entry.Id);
            if (!isNew)
            {
                var index = _entries.FindIndex(e => e.Id == entry.Id);
                if (index >= 0)
                    _entries[index] = entry;
            }
            else
            {
                _entries.Insert(InsertionIndex(entry.Start), entry);
            }

            _byId[entry.Id] = entry;
        }

        if (isNew)
            EntryAdded?.Invoke(entry);
        else
            EntryUpdated?.Invoke(entry);
    }

    private void OnEntryUpdated(TranscriptEntry entry)
    {
        lock (_sync)
        {
            if (!_byId.ContainsKey(entry.Id))
                return;

            var index = _entries.FindIndex(e => e.Id == entry.Id);
            if (index >= 0)
                _entries[index] = entry;

            _byId[entry.Id] = entry;
        }

        EntryUpdated?.Invoke(entry);
    }

    /// <summary>Keeps the transcript in true time order despite out-of-order completion.</summary>
    private int InsertionIndex(TimeSpan start)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Start <= start)
                return i + 1;
        }

        return 0;
    }

    private void OnPipelineFaulted(Exception exception)
    {
        _logger.LogError(exception, "Pipeline faulted");
        SetState(SessionState.Faulted);
        Faulted?.Invoke(exception);
    }

    private async Task TearDownAsync()
    {
        foreach (var pipeline in _pipelines)
        {
            pipeline.EntryProduced -= OnEntryProduced;
            pipeline.EntryUpdated -= OnEntryUpdated;
            pipeline.Faulted -= OnPipelineFaulted;
            await pipeline.DisposeAsync().ConfigureAwait(false);
        }

        _pipelines.Clear();
        _crossTalkGate?.Reset();
    }

    private void SetState(SessionState state)
    {
        _state = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await TearDownAsync().ConfigureAwait(false);
    }
}

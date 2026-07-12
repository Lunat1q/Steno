using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
using Steno.Core.Segmentation;
using Steno.Core.Transcription;

namespace Steno.Core.Session;

/// <summary>
/// One audio channel, end to end:
///   capture → VAD segmenter → queue → whisper.cpp → TranscriptEntry
///
/// Two of these run concurrently (mic and loopback) with independent whisper contexts,
/// which is what makes per-speaker transcription free — see ADR 0002.
/// </summary>
public sealed class ChannelPipeline : IAsyncDisposable
{
    /// <summary>Partials are droppable; finals never are. Past this backlog, stop queueing partials.</summary>
    private const int MaxPendingBeforeDroppingPartials = 1;

    /// <summary>20 ms frames × 5 ≈ 10 level updates a second. Enough to look live, cheap to render.</summary>
    private const int LevelReportEveryNthFrame = 5;

    private readonly SpeakerChannel _channel;
    private readonly IAudioCaptureSource _capture;
    private readonly UtteranceSegmenter _segmenter;
    private readonly ITranscriber _transcriber;
    private readonly ISpeakerResolver _speakerResolver;
    private readonly TranscriptionOptions _options;
    private readonly CrossTalkGate? _crossTalkGate;
    private readonly ILogger _logger;

    private readonly Channel<Utterance> _queue =
        System.Threading.Channels.Channel.CreateUnbounded<Utterance>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly CancellationTokenSource _cts = new();
    private Task? _worker;
    private int _pending;
    private float _level;
    private int _framesSinceLevelReport;
    private volatile bool _paused;

    public ChannelPipeline(
        SpeakerChannel channel,
        IAudioCaptureSource capture,
        UtteranceSegmenter segmenter,
        ITranscriber transcriber,
        ISpeakerResolver speakerResolver,
        TranscriptionOptions options,
        CrossTalkGate? crossTalkGate,
        ILogger logger)
    {
        _channel = channel;
        _capture = capture;
        _segmenter = segmenter;
        _transcriber = transcriber;
        _speakerResolver = speakerResolver;
        _options = options;
        _crossTalkGate = crossTalkGate;
        _logger = logger;

        _capture.FrameAvailable += OnFrame;
        _capture.Faulted += OnFaulted;
        _segmenter.UtteranceReady += OnUtteranceReady;
    }

    /// <summary>A new entry (partial or final) is available.</summary>
    public event Action<TranscriptEntry>? EntryProduced;

    /// <summary>An existing entry changed — a partial became final, or a translation arrived.</summary>
    public event Action<TranscriptEntry>? EntryUpdated;

    /// <summary>Smoothed 0..1 loudness, ~10 Hz. Lets the user see that the channel is actually alive.</summary>
    public event Action<float>? LevelChanged;

    public event Action<Exception>? Faulted;

    public async Task StartAsync(DateTimeOffset clockOrigin, CancellationToken cancellationToken = default)
    {
        _worker = Task.Run(() => ConsumeAsync(_cts.Token), CancellationToken.None);
        await _capture.StartAsync(clockOrigin, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        await _capture.StopAsync().ConfigureAwait(false);
        _segmenter.Flush();       // don't lose speech that was still open when the call ended
        _queue.Writer.TryComplete();

        if (_worker is not null)
            await _worker.ConfigureAwait(false);
    }

    /// <summary>
    /// Pause keeps the capture device open and the clock running, but stops audio reaching the
    /// segmenter — so nothing said during a break is ever transcribed, and the transcript's
    /// timeline still lines up with the call afterwards. Meters keep moving, because the point
    /// of a pause indicator is to prove the app is alive but *not listening*.
    /// </summary>
    public bool IsPaused
    {
        get => _paused;
        set
        {
            if (_paused == value)
                return;

            _paused = value;

            // Close whatever sentence was in flight, rather than stitching it to whatever gets
            // said after the break.
            if (value)
                _segmenter.Flush();
        }
    }

    private void OnFrame(AudioFrame frame)
    {
        ReportLevel(frame);

        if (_paused)
            return;

        _crossTalkGate?.Observe(_channel, frame);
        _segmenter.Push(frame);
    }

    /// <summary>
    /// Feeds the UI's level meter. Every 20 ms would be 50 UI updates/sec per channel for no
    /// benefit, so report every 5th frame (~10 Hz) and smooth it — a raw RMS meter flickers.
    /// </summary>
    private void ReportLevel(AudioFrame frame)
    {
        var rms = EnergyVoiceActivityDetector.Rms(frame.Samples);

        // Perceptual, not linear: speech RMS lives around 0.05–0.3, which a linear bar
        // renders as a permanently empty meter.
        var normalized = Math.Clamp(MathF.Sqrt(rms) * 2.2f, 0f, 1f);
        _level += (normalized - _level) * (normalized > _level ? 0.6f : 0.15f);

        if (++_framesSinceLevelReport < LevelReportEveryNthFrame)
            return;

        _framesSinceLevelReport = 0;
        LevelChanged?.Invoke(_level);
    }

    private void OnFaulted(Exception exception) => Faulted?.Invoke(exception);

    private void OnUtteranceReady(Utterance utterance)
    {
        if (utterance.Kind == UtteranceKind.Partial &&
            Volatile.Read(ref _pending) > MaxPendingBeforeDroppingPartials)
        {
            return; // whisper is behind; a stale partial is worth less than catching up
        }

        // Echo check happens here, not in the worker: no point spending inference on it.
        if (_channel == SpeakerChannel.Local && _crossTalkGate?.IsEcho(utterance) == true)
        {
            _logger.LogDebug("Dropped mic utterance at {Start} as loudspeaker echo", utterance.Start);
            return;
        }

        if (_queue.Writer.TryWrite(utterance))
            Interlocked.Increment(ref _pending);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var utterance in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await TranscribeAsync(utterance, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Transcription failed on {Channel}", _channel);
                    Faulted?.Invoke(ex);
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping. Expected.
        }
    }

    private async Task TranscribeAsync(Utterance utterance, CancellationToken cancellationToken)
    {
        var isDraft = utterance.Kind == UtteranceKind.Partial;

        var result = await _transcriber
            .TranscribeAsync(utterance.Samples, isDraft, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsEmpty || result.NoSpeechProbability > _options.NoSpeechThreshold)
            return; // VAD let noise through; whisper.cpp would answer with confident nonsense

        var entry = new TranscriptEntry(
            utterance.Id,
            _channel,
            _speakerResolver.Resolve(_channel, utterance),
            utterance.Start,
            utterance.End,
            result.Text,
            Translation: null,
            result.Confidence,
            utterance.Kind == UtteranceKind.Final,
            result.Tokens);

        EntryProduced?.Invoke(entry);

        if (utterance.Kind != UtteranceKind.Final || _options.Translation != TranslationMode.ToEnglish)
            return;

        // Second whisper pass, English only (ADR 0005). Runs after the transcript is already
        // on screen, so translation never delays what the user is reading.
        var translation = await _transcriber
            .TranslateAsync(utterance.Samples, cancellationToken)
            .ConfigureAwait(false);

        if (!translation.IsEmpty)
            EntryUpdated?.Invoke(entry with { Translation = translation.Text });
    }

    public async ValueTask DisposeAsync()
    {
        _capture.FrameAvailable -= OnFrame;
        _capture.Faulted -= OnFaulted;
        _segmenter.UtteranceReady -= OnUtteranceReady;

        await _cts.CancelAsync().ConfigureAwait(false);
        await _capture.DisposeAsync().ConfigureAwait(false);
        await _transcriber.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}

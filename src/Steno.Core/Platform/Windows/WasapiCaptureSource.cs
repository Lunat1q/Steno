using NAudio.CoreAudioApi;
using NAudio.Wave;
using Steno.Core.Abstractions;
using Steno.Core.Audio;

namespace Steno.Core.Platform.Windows;

/// <summary>
/// WASAPI capture for one endpoint, normalised to 16 kHz mono float32 (ADR 0004).
///
/// A <see cref="AudioDeviceKind.Capture"/> device gives the local microphone; a
/// <see cref="AudioDeviceKind.Render"/> device is captured in *loopback* mode, which
/// yields whatever the machine is playing — i.e. the remote party, whatever app the
/// call runs in.
/// </summary>
public sealed class WasapiCaptureSource : IAudioCaptureSource
{
    /// <summary>Loopback devices go silent (deliver no callbacks) when nothing is playing.
    /// Past this much drift we synthesise silence so the timeline stays aligned with the mic.</summary>
    private static readonly TimeSpan MaxClockDrift = TimeSpan.FromMilliseconds(200);

    /// <summary>How often the silence filler checks the clock while the device says nothing.</summary>
    private static readonly TimeSpan SilenceTick = TimeSpan.FromMilliseconds(100);

    /// <summary>Device buffer. Pure latency before a word even reaches the VAD (ADR 0014).</summary>
    private const int CaptureBufferMs = 40;

    private readonly string _deviceId;
    private readonly bool _loopback;
    private readonly object _sync = new();
    private readonly object _emitSync = new();

    private Timer? _silenceTimer;
    private MMDevice? _device;
    private WasapiCapture? _capture;
    private AudioStreamNormalizer? _normalizer;
    private DateTimeOffset _clockOrigin;
    private long _emittedSamples;
    private TaskCompletionSource? _stopped;

    /// <param name="deviceId">
    /// The endpoint id, not an MMDevice. The COM object is resolved later, inside the MTA —
    /// see <see cref="InMtaAsync"/> for why it cannot simply be handed to us.
    /// </param>
    public WasapiCaptureSource(string deviceId, AudioDeviceKind kind)
    {
        _deviceId = deviceId;
        _loopback = kind == AudioDeviceKind.Render;
    }

    public event Action<AudioFrame>? FrameAvailable;
    public event Action<Exception>? Faulted;

    public bool IsCapturing { get; private set; }

    public Task StartAsync(DateTimeOffset clockOrigin, CancellationToken cancellationToken = default) =>
        InMtaAsync(() =>
        {
            lock (_sync)
            {
                if (IsCapturing)
                    return;

                _clockOrigin = clockOrigin;
                _emittedSamples = 0;
                _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                using var enumerator = new MMDeviceEnumerator();
                _device = enumerator.GetDevice(_deviceId);

                // NAudio's default capture buffer is 100 ms, and every millisecond of it is pure
                // latency before a word even reaches the VAD. 40 ms stays comfortably above the
                // WASAPI period, so it does not risk glitching. Event-sync mode wakes us as soon
                // as the device has data instead of polling.
                _capture = _loopback
                    ? new WasapiLoopbackCapture(_device)
                    : new WasapiCapture(_device, useEventSync: true, CaptureBufferMs)
                    {
                        ShareMode = AudioClientShareMode.Shared
                    };

                _normalizer = new AudioStreamNormalizer(_capture.WaveFormat);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                IsCapturing = true;

                _silenceTimer = new Timer(OnSilenceTick, null, SilenceTick, SilenceTick);
            }
        });

    public async Task StopAsync()
    {
        WasapiCapture? capture;
        Task stopped;

        lock (_sync)
        {
            if (!IsCapturing || _capture is null || _stopped is null)
                return;

            IsCapturing = false;
            capture = _capture;
            stopped = _stopped.Task;
        }

        await InMtaAsync(capture.StopRecording).ConfigureAwait(false);
        await stopped.ConfigureAwait(false);
    }

    /// <summary>
    /// Runs WASAPI work on an MTA thread — always the same apartment, never the UI's.
    ///
    /// IMMDevice has no registered proxy/stub, so the runtime cannot marshal it between COM
    /// apartments: touch a device created on Avalonia's STA UI thread from a thread-pool
    /// thread and QueryInterface fails with E_NOINTERFACE. Thread-pool threads are all MTA
    /// and MTA is a single process-wide apartment, so keeping every WASAPI call inside
    /// Task.Run keeps the device, the capture object and the audio callbacks in one apartment.
    /// </summary>
    private static Task InMtaAsync(Action action) =>
        Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA
            ? RunInline(action)
            : Task.Run(action);

    private static Task RunInline(Action action)
    {
        try
        {
            action();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            lock (_emitSync)
            {
                if (_normalizer is null || e.BytesRecorded == 0)
                    return;

                PadForDroppedTime();

                foreach (var chunk in _normalizer.Normalize(e.Buffer, e.BytesRecorded))
                    Emit(chunk);
            }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    /// <summary>
    /// A render device that is playing nothing raises **no** WASAPI callbacks at all — so this
    /// cannot live in OnDataAvailable, or a silent remote channel would emit nothing, its clock
    /// would freeze behind the microphone's, and every later line would be timestamped too
    /// early. A timer keeps the channel producing silence at wall-clock rate whether or not the
    /// device says anything, which also lets the VAD see the pause as the pause it really was.
    /// </summary>
    private void OnSilenceTick(object? state)
    {
        try
        {
            lock (_emitSync)
            {
                if (!IsCapturing)
                    return;

                PadForDroppedTime();
            }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex);
        }
    }

    /// <summary>Fills the gap between what the device gave us and how much time actually passed.</summary>
    private void PadForDroppedTime()
    {
        var expected = DateTimeOffset.UtcNow - _clockOrigin;
        var actual = AudioConstants.DurationOf((int)_emittedSamples);
        var drift = expected - actual;

        if (drift <= MaxClockDrift)
            return;

        var missing = AudioConstants.SamplesFor(drift);
        for (var emitted = 0; emitted < missing; emitted += AudioConstants.FrameSamples)
            Emit(new float[AudioConstants.FrameSamples]);
    }

    /// <summary>Callers hold <see cref="_emitSync"/>: the capture thread and the silence timer both emit.</summary>
    private void Emit(float[] samples)
    {
        var offset = AudioConstants.DurationOf((int)_emittedSamples);
        _emittedSamples += samples.Length;
        FrameAvailable?.Invoke(new AudioFrame(samples, offset));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_sync)
        {
            IsCapturing = false;

            _silenceTimer?.Dispose();
            _silenceTimer = null;

            if (_capture is not null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            // Released here, on NAudio's own (MTA) capture thread — the apartment that created it.
            _device?.Dispose();
            _device = null;
            _normalizer = null;
            _stopped?.TrySetResult();
        }

        // A non-null exception here means the device died (unplugged, format change,
        // exclusive-mode grab) rather than us calling Stop.
        if (e.Exception is not null)
            Faulted?.Invoke(e.Exception);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}

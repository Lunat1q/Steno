using Microsoft.Extensions.Logging.Abstractions;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
using Steno.Core.Segmentation;
using Steno.Core.Session;
using Steno.Core.Transcription;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>
/// Pause is a promise: a break, or a stretch of the call nobody wants recorded, must never
/// reach the transcription engine. A pause that merely hides the text would be a privacy bug,
/// so the test asserts on what the *transcriber* was handed, not on what the UI shows.
/// </summary>
public class PausedPipelineTests
{
    [Fact]
    public async Task Audio_captured_while_paused_never_reaches_the_transcriber()
    {
        var capture = new FakeCapture();
        var transcriber = new RecordingTranscriber();
        var pipeline = BuildPipeline(capture, transcriber);

        await pipeline.StartAsync(DateTimeOffset.UtcNow);

        // Speak → pause → speak → resume → speak. Only the first and last should be transcribed.
        await SpeakAsync(capture);
        await transcriber.WaitForCallsAsync(1);

        pipeline.IsPaused = true;
        var callsWhenPaused = transcriber.Count;

        await SpeakAsync(capture);
        await Task.Delay(300); // give a broken implementation every chance to transcribe it

        Assert.Equal(callsWhenPaused, transcriber.Count);

        pipeline.IsPaused = false;
        await SpeakAsync(capture);
        await transcriber.WaitForCallsAsync(callsWhenPaused + 1);

        Assert.True(transcriber.Count > callsWhenPaused, "resuming must start transcribing again");

        await pipeline.StopAsync();
        await pipeline.DisposeAsync();
    }

    [Fact]
    public async Task Pausing_closes_the_sentence_in_flight_instead_of_stitching_across_the_break()
    {
        var capture = new FakeCapture();
        var transcriber = new RecordingTranscriber();
        var pipeline = BuildPipeline(capture, transcriber);

        var entries = new List<TranscriptEntry>();
        pipeline.EntryProduced += entries.Add;

        await pipeline.StartAsync(DateTimeOffset.UtcNow);

        // Speech that is still ongoing — no trailing silence, so nothing has closed it yet.
        capture.Push(AudioSignals.Tone(TimeSpan.FromSeconds(1)));

        pipeline.IsPaused = true;
        await transcriber.WaitForCallsAsync(1);

        // The half-finished sentence must be flushed at the pause, not left to be glued onto
        // whatever gets said after the break.
        Assert.NotEmpty(entries);

        await pipeline.StopAsync();
        await pipeline.DisposeAsync();
    }

    private static ChannelPipeline BuildPipeline(FakeCapture capture, ITranscriber transcriber) =>
        new(SpeakerChannel.Local,
            capture,
            new UtteranceSegmenter(new EnergyVoiceActivityDetector(), new SegmentationOptions()),
            transcriber,
            new ChannelSpeakerResolver("Me", "Them"),
            new TranscriptionOptions(),
            crossTalkGate: null,
            NullLogger.Instance);

    private static async Task SpeakAsync(FakeCapture capture)
    {
        capture.Push(AudioSignals.Concat(
            AudioSignals.Tone(TimeSpan.FromSeconds(1)),
            AudioSignals.Silence(TimeSpan.FromSeconds(1))));

        await Task.Yield();
    }

    private sealed class FakeCapture : IAudioCaptureSource
    {
        public event Action<AudioFrame>? FrameAvailable;
        public event Action<Exception>? Faulted;

        public bool IsCapturing { get; private set; }

        public Task StartAsync(DateTimeOffset clockOrigin, CancellationToken cancellationToken = default)
        {
            IsCapturing = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsCapturing = false;
            return Task.CompletedTask;
        }

        public void Push(float[] samples)
        {
            foreach (var frame in AudioSignals.ToFrames(samples))
                FrameAvailable?.Invoke(new AudioFrame(frame.Samples, _offset + frame.Offset));

            _offset += AudioConstants.DurationOf(samples.Length);
        }

        private TimeSpan _offset = TimeSpan.Zero;

        public ValueTask DisposeAsync()
        {
            _ = Faulted; // silence the unused-event warning; a fake never faults
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingTranscriber : ITranscriber
    {
        private readonly SemaphoreSlim _called = new(0);
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Task<TranscriptionResult> TranscribeAsync(
            ReadOnlyMemory<float> samples,
            bool draft = false,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            _called.Release();

            return Task.FromResult(new TranscriptionResult(
                "да", 0.9f, 0.01f, "ru", [new TranscriptToken("да", 0.9f)]));
        }

        public Task<TranscriptionResult> TranslateAsync(
            ReadOnlyMemory<float> samples,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(TranscriptionResult.Empty);

        public async Task WaitForCallsAsync(int target)
        {
            while (Count < target)
            {
                var signalled = await _called.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(signalled, $"transcriber was called {Count} times, expected {target}");
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

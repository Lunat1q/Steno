using Steno.Core.Transcription;
using Steno.Core.Whisper;
using Xunit;

namespace Steno.Core.Tests;

public class WhisperBenchmarkTests
{
    [Fact]
    public void Summarize_recommends_nothing_to_change_when_there_is_no_gpu()
    {
        var summary = WhisperBenchmark.Summarize(
        [
            new BackendSpeed(ComputeBackend.Cpu, "CPU", TimeSpan.FromSeconds(5.9))
        ]);

        Assert.Equal("No GPU whisper can use on this machine. CPU: 5.9 s a sentence.", summary);
    }

    [Fact]
    public void Summarize_names_the_winner_and_the_factor()
    {
        var summary = WhisperBenchmark.Summarize(
        [
            new BackendSpeed(ComputeBackend.Gpu, "GPU (Vulkan)", TimeSpan.FromSeconds(0.4)),
            new BackendSpeed(ComputeBackend.Cpu, "CPU", TimeSpan.FromSeconds(5.6))
        ]);

        Assert.Equal(
            "GPU (Vulkan): 0.4 s a sentence — 14.0× faster than CPU (5.6 s). Set to GPU (Vulkan).",
            summary);
    }

    /// <summary>
    /// The case this whole feature exists for: an integrated GPU that loses to its own CPU. The
    /// benchmark must be willing to say so — a hard-coded "GPU is faster" would not.
    /// </summary>
    [Fact]
    public void Summarize_is_happy_to_recommend_the_cpu()
    {
        var summary = WhisperBenchmark.Summarize(
        [
            new BackendSpeed(ComputeBackend.Cpu, "CPU", TimeSpan.FromSeconds(8)),
            new BackendSpeed(ComputeBackend.Gpu, "GPU (Vulkan)", TimeSpan.FromSeconds(12))
        ]);

        Assert.StartsWith("CPU: 8.0 s a sentence — 1.5× faster than GPU (Vulkan)", summary);
    }

    /// <summary>Desktop GPU: turbo lands at 0.19 s, so the bigger model fits inside the budget too.</summary>
    [Fact]
    public void Advise_offers_the_biggest_model_that_still_keeps_up()
    {
        var advice = WhisperBenchmark.Advise(
            WhisperModel.LargeV3Turbo,
            new BackendSpeed(ComputeBackend.Gpu, "GPU (Vulkan)", TimeSpan.FromMilliseconds(186)));

        Assert.NotNull(advice);
        Assert.Equal(WhisperModel.LargeV3, advice.Model);
        Assert.True(advice.KeepsUpLive);
        Assert.InRange(advice.Estimate.TotalMilliseconds, 350, 450); // measured: 396 ms
    }

    /// <summary>
    /// Same machine, CPU: turbo takes 10.7 s and even the small model is extrapolated to 2.1 s —
    /// over the live budget. The advice has to admit that rather than pretend Fast is live.
    /// </summary>
    [Fact]
    public void Advise_says_so_when_nothing_keeps_up()
    {
        var advice = WhisperBenchmark.Advise(
            WhisperModel.LargeV3Turbo,
            new BackendSpeed(ComputeBackend.Cpu, "CPU", TimeSpan.FromSeconds(10.7)));

        Assert.NotNull(advice);
        Assert.Equal(WhisperModel.Small, advice.Model);
        Assert.False(advice.KeepsUpLive);
        Assert.InRange(advice.Estimate.TotalSeconds, 1.9, 2.3); // measured: 2.1 s
    }

    /// <summary>Extrapolation runs from whichever model was measured, not only from turbo.</summary>
    [Fact]
    public void Advise_extrapolates_up_from_the_small_model()
    {
        var advice = WhisperBenchmark.Advise(
            WhisperModel.Small,
            new BackendSpeed(ComputeBackend.Gpu, "GPU (Vulkan)", TimeSpan.FromMilliseconds(121)));

        Assert.NotNull(advice);
        Assert.Equal(WhisperModel.LargeV3, advice.Model);
        Assert.InRange(advice.Estimate.TotalMilliseconds, 350, 450);
    }

    [Fact]
    public void Advise_has_nothing_to_say_about_a_model_it_has_never_measured()
    {
        Assert.Null(WhisperBenchmark.Advise(
            WhisperModel.Tiny,
            new BackendSpeed(ComputeBackend.Cpu, "CPU", TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Sample_utterance_is_five_seconds_of_audible_speech_level_audio()
    {
        var audio = WhisperBenchmark.SampleUtterance();

        Assert.Equal(16_000 * 5, audio.Length);
        Assert.True(TranscriptionPolicy.IsWorthTranscribing(audio));
        Assert.All(audio, sample => Assert.InRange(sample, -1f, 1f));
    }
}

using System.Diagnostics;
using Steno.Core.Audio;
using Steno.Core.Transcription;
using Whisper.net;

namespace Steno.Core.Whisper;

/// <summary>How long one utterance takes on one processor, on this machine, with this model.</summary>
/// <param name="Backend">The setting that produces this result.</param>
/// <param name="Name">"GPU (Vulkan)" or "CPU".</param>
public sealed record BackendSpeed(ComputeBackend Backend, string Name, TimeSpan PerUtterance);

/// <summary>Which model this machine should be running, extrapolated from the one that was measured.</summary>
/// <param name="Estimate">Per utterance, on the winning backend. Estimated, not measured.</param>
/// <param name="KeepsUpLive">False when no model is fast enough here and the honest answer is "record it".</param>
public sealed record ModelAdvice(WhisperModel Model, TimeSpan Estimate, bool KeepsUpLive);

/// <param name="Results">Fastest first. One entry on a machine with no usable GPU.</param>
/// <param name="Recommended">What the switch should be set to.</param>
/// <param name="Summary">One line, for the user.</param>
/// <param name="Advice">Null if the model that was measured is not one we have cost data for.</param>
public sealed record BenchmarkReport(
    IReadOnlyList<BackendSpeed> Results,
    ComputeBackend Recommended,
    string Summary,
    ModelAdvice? Advice);

/// <summary>
/// Times the chosen model on the CPU and on the GPU, on this machine, and says which to use.
///
/// This exists because the obvious answer is wrong often enough to matter: a GPU is ~65× faster
/// than a desktop CPU here, but a low-end laptop's integrated GPU shares system memory with
/// everything else and can come out *slower* than the same laptop's CPU — while also making the
/// screen stutter for the duration of the call. Nobody can tell those two machines apart from
/// the spec sheet, so we measure instead of guessing (ADR 0024).
/// </summary>
public static class WhisperBenchmark
{
    /// <summary>
    /// Both backends, sequentially: CPU first (which also resolves the native library and so
    /// tells us whether a GPU exists at all), then GPU if there is one. The model is loaded and
    /// unloaded around each — two copies of 1.5 GB of weights at once is exactly the wrong thing
    /// to do to the 8 GB laptop this feature is for.
    /// </summary>
    public static Task<BenchmarkReport> RunAsync(
        string modelPath,
        WhisperModel model,
        string language,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default) =>
        // Every call inside is a blocking native one; keep the lot off the UI thread.
        Task.Run(async () =>
        {
            var audio = SampleUtterance();
            var results = new List<BackendSpeed>();

            status?.Report("Testing the CPU…");
            var cpu = await MeasureAsync(modelPath, useGpu: false, language, audio, cancellationToken)
                .ConfigureAwait(false);
            results.Add(new BackendSpeed(ComputeBackend.Cpu, "CPU", cpu));

            if (WhisperRuntime.GpuAvailable)
            {
                status?.Report("Testing the GPU…");
                var gpu = await MeasureAsync(modelPath, useGpu: true, language, audio, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(new BackendSpeed(ComputeBackend.Gpu, WhisperRuntime.Describe(true), gpu));
            }

            results.Sort((a, b) => a.PerUtterance.CompareTo(b.PerUtterance));

            return new BenchmarkReport(
                results,
                results[0].Backend,
                Summarize(results),
                Advise(model, results[0]));
        }, cancellationToken);

    /// <summary>
    /// Per-utterance cost of each offered model, relative to large-v3-turbo, on each processor.
    ///
    /// Two tables, because one would be wrong. Measured (9950X3D + RTX 5090, 5 s utterance):
    /// small 2127 / turbo 10705 / large-v3 12117 ms on the CPU, and 121 / 186 / 396 ms on the GPU.
    /// The shapes are nothing alike. whisper's encoder dominates on a CPU, and turbo and large-v3
    /// share an encoder — so paying for large-v3's 32 decoder layers instead of turbo's 4 costs a
    /// CPU only 13%, while on a GPU, where the encoder is nearly free, it more than doubles the bill.
    /// Downwards it inverts: small saves a CPU 80% and a GPU only 35%, because at that size the GPU
    /// is mostly waiting on fixed overheads.
    ///
    /// ponytail: ratios from one machine, used to rank three models — not to predict milliseconds.
    /// If a laptop's iGPU ever ranks them differently from what it then measures, replace this with
    /// a real second run at the recommended model.
    /// </summary>
    private static readonly Dictionary<WhisperModel, double> CpuCost = new()
    {
        [WhisperModel.Small] = 0.20,
        [WhisperModel.LargeV3Turbo] = 1.00,
        [WhisperModel.LargeV3] = 1.13
    };

    private static readonly Dictionary<WhisperModel, double> GpuCost = new()
    {
        [WhisperModel.Small] = 0.65,
        [WhisperModel.LargeV3Turbo] = 1.00,
        [WhisperModel.LargeV3] = 2.13
    };

    /// <summary>
    /// The most a sentence may take and still feel live. Above this the pipeline is transcribing
    /// the last sentence while the next one is already being spoken, and the transcript drifts
    /// further behind the call the longer it runs (ADR 0010).
    /// </summary>
    internal static readonly TimeSpan LiveBudget = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// The biggest model this machine can still run live — or, when it can't run any of them live,
    /// the fastest one, flagged so the UI can say so rather than recommend a fantasy.
    /// </summary>
    internal static ModelAdvice? Advise(WhisperModel measured, BackendSpeed best)
    {
        var costs = best.Backend == ComputeBackend.Gpu ? GpuCost : CpuCost;

        if (!costs.TryGetValue(measured, out var measuredCost))
            return null;

        // What one unit of "turbo-equivalent" work costs on this machine, on this processor.
        var unit = best.PerUtterance / measuredCost;

        var estimates = costs
            .OrderBy(entry => entry.Value)
            .Select(entry => new ModelAdvice(entry.Key, unit * entry.Value, unit * entry.Value <= LiveBudget))
            .ToList();

        return estimates.LastOrDefault(estimate => estimate.KeepsUpLive) ?? estimates[0];
    }

    /// <summary>Fastest wins. No tie-breaking rule: a difference too small to matter is too small to argue about.</summary>
    internal static string Summarize(IReadOnlyList<BackendSpeed> results)
    {
        var best = results[0];
        var seconds = $"{best.PerUtterance.TotalSeconds:0.0} s a sentence";

        if (results.Count == 1)
            return $"No GPU whisper can use on this machine. CPU: {seconds}.";

        var other = results[1];
        var factor = other.PerUtterance / best.PerUtterance;

        return $"{best.Name}: {seconds} — {factor:0.0}× faster than {other.Name} " +
               $"({other.PerUtterance.TotalSeconds:0.0} s). Set to {best.Name}.";
    }

    private static async Task<TimeSpan> MeasureAsync(
        string modelPath,
        bool useGpu,
        string language,
        float[] audio,
        CancellationToken cancellationToken)
    {
        WhisperRuntime.Configure();

        using var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = useGpu });

        await using var processor = factory.CreateBuilder()
            .WithNoContext()
            .WithLanguage(string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase) ? "en" : language)
            .Build();

        // The first inference on a GPU pays for Vulkan's pipeline compilation and is several
        // times slower than the rest — the app pays it in "Warming up…", so the benchmark must
        // not charge it to the GPU's score.
        await ProcessAsync(processor, audio, cancellationToken).ConfigureAwait(false);

        // ponytail: one timed run, not a best-of-N. This decides between numbers that differ by
        // multiples; run-to-run noise does not. Take a median if that ever stops being true.
        var stopwatch = Stopwatch.StartNew();
        await ProcessAsync(processor, audio, cancellationToken).ConfigureAwait(false);

        return stopwatch.Elapsed;
    }

    private static async Task ProcessAsync(
        WhisperProcessor processor,
        float[] audio,
        CancellationToken cancellationToken)
    {
        await foreach (var _ in processor.ProcessAsync(audio, cancellationToken).ConfigureAwait(false))
        {
            // The text is irrelevant; only the clock matters.
        }
    }

    /// <summary>
    /// Five seconds of synthetic, speech-shaped audio — a voiced pitch with formants, chopped
    /// into syllables.
    ///
    /// It is not speech, and the words that come back are nonsense. That is acceptable, because
    /// whisper's cost is dominated by the encoder, which always runs over a padded 30 s window
    /// regardless of what is in it: the number this produces tracks the number a real sentence
    /// produces, and both backends are handed exactly the same buffer.
    ///
    /// ponytail: shipping a real 5 s WAV would measure the decoder honestly too. Do that if the
    /// benchmark ever disagrees with the app's actual latency by more than it should.
    /// </summary>
    internal static float[] SampleUtterance()
    {
        const int seconds = 5;
        const float pitch = 130f;

        var samples = new float[AudioConstants.SampleRate * seconds];
        var random = new Random(20240724);

        for (var i = 0; i < samples.Length; i++)
        {
            var t = (float)i / AudioConstants.SampleRate;

            // ~4 syllables a second, each with a soft attack and decay: silence between words is
            // what stops whisper treating the whole buffer as one long tone.
            var syllable = MathF.Max(0f, MathF.Sin(2f * MathF.PI * 4f * t));

            var voice =
                MathF.Sin(2f * MathF.PI * pitch * t) * 0.5f +
                MathF.Sin(2f * MathF.PI * 700f * t) * 0.3f +   // first formant
                MathF.Sin(2f * MathF.PI * 1800f * t) * 0.15f + // second formant
                (float)(random.NextDouble() - 0.5) * 0.05f;    // breath

            samples[i] = voice * syllable * TranscriptionPolicy.TargetRms * 3f;
        }

        return samples;
    }
}

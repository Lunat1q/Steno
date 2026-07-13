using Steno.Core.Transcription;
using Whisper.net.LibraryLoader;

namespace Steno.Core.Whisper;

/// <summary>
/// Picks which whisper.cpp native backend gets loaded, and reports which one won.
///
/// This single choice is worth more than every other latency knob in the app combined:
/// on a 16-core desktop CPU, large-v3-turbo takes ~11 s per utterance; on a discrete GPU via
/// Vulkan it takes ~170 ms. But "GPU" is not automatically the fast answer — on a low-end
/// laptop's integrated GPU it can be slower than the same machine's CPU, which is why the
/// choice is a user setting with a benchmark behind it (ADR 0024).
/// </summary>
public static class WhisperRuntime
{
    private static readonly object Sync = new();
    private static bool _configured;

    /// <summary>
    /// Vulkan, then CPU — deliberately *not* Whisper.net's default order, which tries CUDA
    /// first. The CUDA build ships no kernels for recent (Blackwell/sm_120) cards and takes the
    /// whole process down with it rather than throwing something catchable. Vulkan covers
    /// NVIDIA, AMD and Intel from one package and degrades to CPU when there is no GPU.
    ///
    /// The Vulkan library is loaded even when the user has chosen CPU: a native library can only
    /// be loaded once per process, and the Vulkan build contains the CPU kernels as well. CPU vs
    /// GPU is therefore a per-model-load flag (WhisperFactoryOptions.UseGpu), not a library
    /// choice — which is what lets the setting take effect, and the benchmark measure both,
    /// without restarting the app.
    /// </summary>
    public static void Configure()
    {
        lock (Sync)
        {
            if (_configured)
                return;

            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
            _configured = true;
        }
    }

    /// <summary>Null until the first model is loaded — the library is chosen at load time.</summary>
    public static RuntimeLibrary? Loaded => RuntimeOptions.LoadedLibrary;

    /// <summary>
    /// Whether the loaded library can drive a GPU at all. Meaningless until a model has been
    /// loaded once, because that is when the native library is resolved.
    /// </summary>
    public static bool GpuAvailable =>
        Loaded is RuntimeLibrary.Vulkan or RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12 or RuntimeLibrary.CoreML;

    /// <summary>Whether a model asked for this backend should be handed to the GPU.</summary>
    public static bool WantsGpu(ComputeBackend backend) => backend is ComputeBackend.Auto or ComputeBackend.Gpu;

    /// <summary>
    /// For the UI: the user needs to know whether they are on the fast path. <paramref name="useGpu" />
    /// is what we asked for; <see cref="GpuAvailable" /> is what we got.
    /// </summary>
    public static string Describe(bool useGpu) => useGpu && GpuAvailable
        ? Loaded switch
        {
            RuntimeLibrary.Vulkan => "GPU (Vulkan)",
            RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12 => "GPU (CUDA)",
            RuntimeLibrary.CoreML => "GPU (CoreML)",
            _ => "GPU"
        }
        : Loaded is null ? "starting up" : "CPU";
}

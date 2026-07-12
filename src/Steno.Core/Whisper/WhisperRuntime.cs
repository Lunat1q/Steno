using Whisper.net.LibraryLoader;

namespace Steno.Core.Whisper;

/// <summary>
/// Picks which whisper.cpp native backend gets loaded, and reports which one won.
///
/// This single choice is worth more than every other latency knob in the app combined:
/// on a 16-core CPU, large-v3-turbo takes ~11 s per utterance; on a GPU via Vulkan it takes
/// ~170 ms. Sub-second transcription is a GPU feature, not a tuning feature (ADR 0010).
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

    /// <summary>Null until the first model is loaded — the backend is chosen at load time.</summary>
    public static RuntimeLibrary? Loaded => RuntimeOptions.LoadedLibrary;

    public static bool IsGpu => Loaded is RuntimeLibrary.Vulkan or RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12;

    /// <summary>For the UI: the user needs to know whether they are on the fast path.</summary>
    public static string Describe() => Loaded switch
    {
        null => "starting up",
        RuntimeLibrary.Vulkan => "GPU (Vulkan)",
        RuntimeLibrary.Cuda or RuntimeLibrary.Cuda12 => "GPU (CUDA)",
        RuntimeLibrary.CoreML => "GPU (CoreML)",
        _ => "CPU"
    };
}

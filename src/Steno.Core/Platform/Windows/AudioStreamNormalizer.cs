using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Steno.Core.Audio;

namespace Steno.Core.Platform.Windows;

/// <summary>
/// Device format → whisper.cpp format. Endpoints hand out 44.1/48/96 kHz, 1–8 channels,
/// float32 or int16/24/32. whisper.cpp accepts exactly one thing: 16 kHz mono float32.
/// This class is where that mess is contained; nothing above it sees a WaveFormat.
/// </summary>
internal sealed class AudioStreamNormalizer
{
    private readonly BufferedWaveProvider _input;
    private readonly ISampleProvider _output;
    private readonly float[] _readBuffer = new float[AudioConstants.FrameSamples * 8];
    private readonly List<float> _pending = new(AudioConstants.FrameSamples * 8);

    public AudioStreamNormalizer(WaveFormat deviceFormat)
    {
        _input = new BufferedWaveProvider(deviceFormat)
        {
            // 5 s of slack. If the pipeline ever falls that far behind, dropping audio is
            // the correct failure — buffering it would only grow the latency forever.
            BufferDuration = TimeSpan.FromSeconds(5),
            DiscardOnBufferOverflow = true,
            ReadFully = false
        };

        ISampleProvider provider = new MonoDownmixSampleProvider(_input.ToSampleProvider());

        if (provider.WaveFormat.SampleRate != AudioConstants.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, AudioConstants.SampleRate);

        _output = provider;
    }

    /// <summary>Feeds raw device bytes in, gets whole 20 ms frames out. Leftovers are carried.</summary>
    public IEnumerable<float[]> Normalize(byte[] buffer, int bytesRecorded)
    {
        _input.AddSamples(buffer, 0, bytesRecorded);

        int read;
        while ((read = _output.Read(_readBuffer, 0, _readBuffer.Length)) > 0)
        {
            _pending.AddRange(_readBuffer.AsSpan(0, read));

            if (read < _readBuffer.Length)
                break; // source drained
        }

        while (_pending.Count >= AudioConstants.FrameSamples)
        {
            var frame = _pending.GetRange(0, AudioConstants.FrameSamples).ToArray();
            _pending.RemoveRange(0, AudioConstants.FrameSamples);
            yield return frame;
        }
    }
}

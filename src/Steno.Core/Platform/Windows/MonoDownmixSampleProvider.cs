using NAudio.Wave;

namespace Steno.Core.Platform.Windows;

/// <summary>
/// Averages N channels down to 1. NAudio's StereoToMonoSampleProvider only handles 2
/// channels; render endpoints are routinely 4/6/8 (surround), and a loopback capture of
/// a 5.1 device would otherwise be dropped.
/// </summary>
internal sealed class MonoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _buffer = [];

    public MonoDownmixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_sourceChannels == 1)
            return _source.Read(buffer, offset, count);

        var needed = count * _sourceChannels;
        if (_buffer.Length < needed)
            _buffer = new float[needed];

        var read = _source.Read(_buffer, 0, needed);
        var produced = read / _sourceChannels;

        for (var i = 0; i < produced; i++)
        {
            var sum = 0f;
            for (var c = 0; c < _sourceChannels; c++)
                sum += _buffer[i * _sourceChannels + c];

            buffer[offset + i] = sum / _sourceChannels;
        }

        return produced;
    }
}

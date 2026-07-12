using NAudio.Wave;
using Steno.Core.Audio;
using Steno.Core.Session;

namespace Steno.Core.Recording;

public interface ICallRecorder : IDisposable
{
    string Path { get; }

    /// <summary>Called from the capture threads, one per channel. Thread-safe.</summary>
    void Write(SpeakerChannel channel, AudioFrame frame);
}

/// <summary>
/// Records the call as ONE 16 kHz stereo WAV: **left = you, right = them**.
///
/// This is the whole design. Two mono files would drift apart and need pairing; a mixed-down
/// mono file would throw away the speaker separation that is this app's entire premise. A stereo
/// file keeps both voices, keeps them aligned, plays in anything, and can be re-transcribed later
/// with per-speaker attribution intact — the offline path just reads the two channels back out.
///
/// Audio is written on the session's timeline, not in arrival order: the two capture devices
/// deliver independently, so frames are placed by their offset and any hole is filled with
/// silence. Otherwise a slow loopback device would shift the other speaker's voice earlier in the
/// file with every gap.
/// </summary>
public sealed class StereoCallRecorder : ICallRecorder
{
    private readonly object _sync = new();
    private readonly WaveFileWriter _writer;
    private readonly ChannelBuffer _local = new();
    private readonly ChannelBuffer _remote = new();

    /// <summary>Only one channel is being captured, so the other must not be waited for.</summary>
    private readonly bool _hasLocal;
    private readonly bool _hasRemote;

    private long _written;
    private bool _disposed;

    public StereoCallRecorder(string path, bool hasLocal, bool hasRemote)
    {
        Path = path;
        _hasLocal = hasLocal;
        _hasRemote = hasRemote;

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        // 16-bit PCM: ~115 MB/hour in stereo. Float32 would double that for accuracy nobody can
        // hear and whisper does not need.
        _writer = new WaveFileWriter(path, new WaveFormat(AudioConstants.SampleRate, 16, 2));
    }

    public string Path { get; }

    public void Write(SpeakerChannel channel, AudioFrame frame)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            var buffer = channel == SpeakerChannel.Local ? _local : _remote;
            buffer.Add(AudioConstants.SamplesFor(frame.Offset), frame.Samples);

            Flush();
        }
    }

    /// <summary>Writes every sample position both channels can now account for.</summary>
    private void Flush()
    {
        var ready = long.MaxValue;

        if (_hasLocal)
            ready = Math.Min(ready, _local.End);

        if (_hasRemote)
            ready = Math.Min(ready, _remote.End);

        if (ready == long.MaxValue || ready <= _written)
            return;

        var count = (int)(ready - _written);
        var interleaved = new byte[count * 2 * sizeof(short)];

        for (var i = 0; i < count; i++)
        {
            var position = _written + i;
            WriteSample(interleaved, i * 4, _hasLocal ? _local.At(position) : 0f);
            WriteSample(interleaved, i * 4 + 2, _hasRemote ? _remote.At(position) : 0f);
        }

        _writer.Write(interleaved, 0, interleaved.Length);
        _written = ready;

        _local.DiscardBefore(_written);
        _remote.DiscardBefore(_written);
    }

    private static void WriteSample(byte[] target, int offset, float sample)
    {
        var value = (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
        target[offset] = (byte)(value & 0xFF);
        target[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _writer.Flush();
            _writer.Dispose();
        }
    }

    /// <summary>Samples for one channel, addressed by absolute position on the session clock.</summary>
    private sealed class ChannelBuffer
    {
        private readonly List<float> _samples = [];
        private long _start;

        public long End => _start + _samples.Count;

        public void Add(long position, float[] samples)
        {
            if (_samples.Count == 0 && position > _start)
                _start = position;

            // A late or gappy device leaves a hole. Fill it with silence rather than letting the
            // audio slide forward in time.
            for (var gap = End; gap < position; gap++)
                _samples.Add(0f);

            if (position < End)
            {
                // Overlap (a device replaying a frame): ignore what we already have.
                var skip = (int)(End - position);
                if (skip >= samples.Length)
                    return;

                _samples.AddRange(samples.AsSpan(skip).ToArray());
                return;
            }

            _samples.AddRange(samples);
        }

        public float At(long position)
        {
            var index = (int)(position - _start);
            return index >= 0 && index < _samples.Count ? _samples[index] : 0f;
        }

        public void DiscardBefore(long position)
        {
            var drop = (int)(position - _start);
            if (drop <= 0)
                return;

            drop = Math.Min(drop, _samples.Count);
            _samples.RemoveRange(0, drop);
            _start += drop;
        }
    }
}

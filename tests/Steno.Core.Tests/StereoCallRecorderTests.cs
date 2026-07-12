using NAudio.Wave;
using Steno.Core.Audio;
using Steno.Core.Recording;
using Steno.Core.Session;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>
/// The recording is only useful if the two speakers stay on their own channels and on the right
/// part of the timeline. If they drift, a re-transcription attributes the wrong words to the
/// wrong person — which is worse than having no recording at all.
/// </summary>
public class StereoCallRecorderTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"steno-rec-{Guid.NewGuid():N}.wav");

    [Fact]
    public void You_go_left_and_they_go_right()
    {
        using (var recorder = new StereoCallRecorder(_path, hasLocal: true, hasRemote: true))
        {
            // A full-scale tone on the mic, silence on the loopback.
            foreach (var frame in AudioSignals.ToFrames(AudioSignals.Tone(TimeSpan.FromMilliseconds(500), 0.5f)))
                recorder.Write(SpeakerChannel.Local, frame);

            foreach (var frame in AudioSignals.ToFrames(AudioSignals.Silence(TimeSpan.FromMilliseconds(500))))
                recorder.Write(SpeakerChannel.Remote, frame);
        }

        var (left, right) = ReadStereo(_path);

        Assert.True(Rms(left) > 0.2f, "the microphone should be on the left channel");
        Assert.True(Rms(right) < 0.01f, "the loopback was silent, so the right channel should be too");
    }

    [Fact]
    public void A_channel_that_arrives_late_does_not_slide_the_other_one_forward()
    {
        using (var recorder = new StereoCallRecorder(_path, hasLocal: true, hasRemote: true))
        {
            // The remote speaks only in the second half of the second.
            foreach (var frame in AudioSignals.ToFrames(AudioSignals.Silence(TimeSpan.FromSeconds(1))))
                recorder.Write(SpeakerChannel.Local, frame);

            var speech = AudioSignals.Tone(TimeSpan.FromMilliseconds(500), 0.5f);
            var offset = TimeSpan.FromMilliseconds(500);

            foreach (var frame in AudioSignals.ToFrames(speech))
                recorder.Write(SpeakerChannel.Remote, frame with { Offset = frame.Offset + offset });
        }

        var (_, right) = ReadStereo(_path);
        var half = right.Length / 2;

        // Their voice must land in the second half of the file, where it was actually spoken —
        // not at the start, which is where it would end up if frames were written in arrival order.
        Assert.True(Rms(right.Take(half).ToArray()) < 0.01f, "the first half should be silent");
        Assert.True(Rms(right.Skip(half).ToArray()) > 0.2f, "their voice belongs in the second half");
    }

    [Fact]
    public void A_microphone_only_session_still_produces_a_playable_file()
    {
        using (var recorder = new StereoCallRecorder(_path, hasLocal: true, hasRemote: false))
        {
            foreach (var frame in AudioSignals.ToFrames(AudioSignals.Tone(TimeSpan.FromMilliseconds(300), 0.4f)))
                recorder.Write(SpeakerChannel.Local, frame);
        }

        var (left, right) = ReadStereo(_path);

        // Nothing must wait forever for a channel that was never being captured.
        Assert.True(Rms(left) > 0.1f);
        Assert.True(Rms(right) < 0.01f);
    }

    private static (float[] Left, float[] Right) ReadStereo(string path)
    {
        using var reader = new AudioFileReader(path);

        Assert.Equal(2, reader.WaveFormat.Channels);
        Assert.Equal(AudioConstants.SampleRate, reader.WaveFormat.SampleRate);

        var samples = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            samples.AddRange(buffer.AsSpan(0, read).ToArray());

        var frames = samples.Count / 2;
        var left = new float[frames];
        var right = new float[frames];

        for (var i = 0; i < frames; i++)
        {
            left[i] = samples[i * 2];
            right[i] = samples[i * 2 + 1];
        }

        return (left, right);
    }

    private static float Rms(float[] samples) =>
        Steno.Core.Segmentation.EnergyVoiceActivityDetector.Rms(samples);

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}

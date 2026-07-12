using Steno.Core.Audio;
using Steno.Core.Segmentation;

namespace Steno.Core.Session;

/// <summary>
/// Cheap defence against loudspeaker echo (ADR 0006): when the remote party comes out of
/// the speakers, the microphone hears them too and the transcript attributes their words
/// to the user — the one error that makes per-speaker transcription worthless.
///
/// Compares the two channels' energy envelopes over the utterance window. If the remote
/// channel was clearly louder for most of it, the mic copy is echo.
///
/// Deliberately conservative: it prefers to let a duplicate through rather than delete
/// something the user actually said. It is not AEC and does not pretend to be.
/// </summary>
public sealed class CrossTalkGate
{
    /// <summary>Remote must be this much louder than the mic before the mic copy counts as echo.</summary>
    private const float DominanceFactor = 1.6f;

    /// <summary>...for at least this fraction of the utterance.</summary>
    private const float OverlapFraction = 0.7f;

    private const float SilenceFloor = 3e-4f;

    private static readonly TimeSpan HistoryWindow = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private readonly Dictionary<SpeakerChannel, Queue<(TimeSpan At, float Rms)>> _history = new()
    {
        [SpeakerChannel.Local] = new Queue<(TimeSpan, float)>(),
        [SpeakerChannel.Remote] = new Queue<(TimeSpan, float)>()
    };

    /// <summary>Called for every captured frame on both channels, from the capture threads.</summary>
    public void Observe(SpeakerChannel channel, AudioFrame frame)
    {
        var rms = EnergyVoiceActivityDetector.Rms(frame.Samples);

        lock (_sync)
        {
            var queue = _history[channel];
            queue.Enqueue((frame.Offset, rms));

            var cutoff = frame.End - HistoryWindow;
            while (queue.Count > 0 && queue.Peek().At < cutoff)
                queue.Dequeue();
        }
    }

    /// <summary>True when this microphone utterance looks like the remote party echoing back.</summary>
    public bool IsEcho(Utterance utterance)
    {
        lock (_sync)
        {
            var local = Window(SpeakerChannel.Local, utterance.Start, utterance.End);
            var remote = Window(SpeakerChannel.Remote, utterance.Start, utterance.End);

            if (local.Count == 0 || remote.Count == 0)
                return false;

            var dominated = 0;
            foreach (var (at, remoteRms) in remote)
            {
                var localRms = NearestRms(local, at);
                if (remoteRms > SilenceFloor && remoteRms > localRms * DominanceFactor)
                    dominated++;
            }

            return (float)dominated / remote.Count >= OverlapFraction;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            foreach (var queue in _history.Values)
                queue.Clear();
        }
    }

    private List<(TimeSpan At, float Rms)> Window(SpeakerChannel channel, TimeSpan start, TimeSpan end) =>
        _history[channel].Where(s => s.At >= start && s.At <= end).ToList();

    private static float NearestRms(List<(TimeSpan At, float Rms)> samples, TimeSpan at)
    {
        var best = samples[0];
        var bestDistance = (best.At - at).Duration();

        foreach (var sample in samples)
        {
            var distance = (sample.At - at).Duration();
            if (distance >= bestDistance)
                continue;

            best = sample;
            bestDistance = distance;
        }

        return best.Rms;
    }
}

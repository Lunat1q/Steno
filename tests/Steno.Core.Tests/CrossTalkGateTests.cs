using Steno.Core.Audio;
using Steno.Core.Segmentation;
using Steno.Core.Session;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>
/// The echo gate protects the one guarantee the app sells: that the speaker label is right
/// (ADR 0006). A false positive deletes the user's words, so "does not over-suppress" matters
/// as much as "catches echo".
/// </summary>
public class CrossTalkGateTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private static CrossTalkGate GateWith(float micAmplitude, float remoteAmplitude)
    {
        var gate = new CrossTalkGate();

        Feed(gate, SpeakerChannel.Local, micAmplitude);
        Feed(gate, SpeakerChannel.Remote, remoteAmplitude);

        return gate;
    }

    private static void Feed(CrossTalkGate gate, SpeakerChannel channel, float amplitude)
    {
        var audio = amplitude > 0
            ? AudioSignals.Tone(Window, amplitude)
            : AudioSignals.Silence(Window);

        foreach (var frame in AudioSignals.ToFrames(audio))
            gate.Observe(channel, frame);
    }

    private static Utterance MicUtterance() =>
        new(Guid.NewGuid(), UtteranceKind.Final, AudioSignals.Tone(Window, 0.05f), TimeSpan.Zero);

    [Fact]
    public void Loud_remote_over_quiet_mic_is_echo()
    {
        // Loudspeakers: the remote party is booming out of them, the mic hears a faint copy.
        var gate = GateWith(micAmplitude: 0.05f, remoteAmplitude: 0.4f);

        Assert.True(gate.IsEcho(MicUtterance()));
    }

    [Fact]
    public void The_user_speaking_alone_is_never_suppressed()
    {
        var gate = GateWith(micAmplitude: 0.3f, remoteAmplitude: 0f);

        Assert.False(gate.IsEcho(MicUtterance()));
    }

    [Fact]
    public void Both_talking_at_once_keeps_the_user()
    {
        // Comparable levels = genuine double-talk, not echo. Keep it: losing real speech is
        // worse than an occasional duplicate.
        var gate = GateWith(micAmplitude: 0.3f, remoteAmplitude: 0.35f);

        Assert.False(gate.IsEcho(MicUtterance()));
    }

    [Fact]
    public void No_remote_history_means_no_suppression()
    {
        // Mic-only session: nothing to compare against, so the gate must stay out of the way.
        var gate = new CrossTalkGate();
        Feed(gate, SpeakerChannel.Local, 0.3f);

        Assert.False(gate.IsEcho(MicUtterance()));
    }
}

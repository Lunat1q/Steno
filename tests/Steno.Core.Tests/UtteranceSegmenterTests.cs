using Steno.Core.Segmentation;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>
/// The segmenter is what turns a non-streaming engine into a live one (ADR 0003).
/// If it mis-cuts, every transcript downstream is wrong, so it is the piece worth testing.
/// </summary>
public class UtteranceSegmenterTests
{
    private static UtteranceSegmenter Build(SegmentationOptions? options = null) =>
        new(new EnergyVoiceActivityDetector(), options ?? new SegmentationOptions());

    private static List<Utterance> Run(UtteranceSegmenter segmenter, float[] audio)
    {
        var utterances = new List<Utterance>();
        segmenter.UtteranceReady += utterances.Add;

        foreach (var frame in AudioSignals.ToFrames(audio))
            segmenter.Push(frame);

        return utterances;
    }

    [Fact]
    public void Speech_between_silences_yields_one_final_utterance()
    {
        var audio = AudioSignals.Concat(
            AudioSignals.Silence(TimeSpan.FromSeconds(1)),
            AudioSignals.Tone(TimeSpan.FromSeconds(2)),
            AudioSignals.Silence(TimeSpan.FromSeconds(1.5)));

        var utterances = Run(Build(), audio);

        var utterance = Assert.Single(utterances);
        Assert.Equal(UtteranceKind.Final, utterance.Kind);

        // Speech ran 1.0s–3.0s. The cut includes pre-roll and the trailing silence the VAD
        // needed to be sure speech ended, so bound it rather than demanding exact edges.
        Assert.InRange(utterance.Start.TotalSeconds, 0.6, 1.05);
        Assert.InRange(utterance.Duration.TotalSeconds, 2.0, 3.2);
    }

    [Fact]
    public void Preroll_keeps_the_first_syllable()
    {
        var audio = AudioSignals.Concat(
            AudioSignals.Silence(TimeSpan.FromSeconds(1)),
            AudioSignals.Tone(TimeSpan.FromSeconds(1)),
            AudioSignals.Silence(TimeSpan.FromSeconds(1.5)));

        var utterance = Assert.Single(Run(Build(new SegmentationOptions { PrerollMs = 300 }), audio));

        // Without pre-roll the utterance would start ~120 ms *after* speech began (the onset
        // evidence window), clipping the first phoneme.
        Assert.True(
            utterance.Start < TimeSpan.FromSeconds(1),
            $"expected the cut to begin before speech at 1.0s, got {utterance.Start}");
    }

    [Fact]
    public void Clicks_shorter_than_the_minimum_are_dropped()
    {
        var audio = AudioSignals.Concat(
            AudioSignals.Silence(TimeSpan.FromSeconds(0.5)),
            AudioSignals.Tone(TimeSpan.FromMilliseconds(160)),
            AudioSignals.Silence(TimeSpan.FromSeconds(1.5)));

        // whisper.cpp invents words when handed a cough. Anything this short must not reach it.
        Assert.Empty(Run(Build(new SegmentationOptions { MinUtteranceMs = 250 }), audio));
    }

    [Fact]
    public void A_speaker_who_never_pauses_is_force_cut()
    {
        var audio = AudioSignals.Concat(
            AudioSignals.Silence(TimeSpan.FromMilliseconds(300)),
            AudioSignals.Tone(TimeSpan.FromSeconds(7)));

        var utterances = Run(Build(new SegmentationOptions { MaxUtteranceMs = 2_000 }), audio);

        Assert.True(utterances.Count >= 3, $"expected the monologue to be cut into chunks, got {utterances.Count}");
        Assert.All(utterances, u => Assert.True(
            u.Duration <= TimeSpan.FromMilliseconds(2_100),
            $"chunk of {u.Duration} exceeded the 2s force-cut"));
    }

    [Fact]
    public void Flush_emits_speech_that_was_still_open_when_the_call_ended()
    {
        var audio = AudioSignals.Concat(
            AudioSignals.Silence(TimeSpan.FromMilliseconds(300)),
            AudioSignals.Tone(TimeSpan.FromSeconds(1)));

        var segmenter = Build();
        var utterances = new List<Utterance>();
        segmenter.UtteranceReady += utterances.Add;

        foreach (var frame in AudioSignals.ToFrames(audio))
            segmenter.Push(frame);

        Assert.Empty(utterances); // still speaking — nothing closed it

        segmenter.Flush();

        Assert.Single(utterances);
        Assert.Equal(UtteranceKind.Final, utterances[0].Kind);
    }

    [Fact]
    public void Partials_are_emitted_while_speech_continues()
    {
        var audio = AudioSignals.Concat(
            AudioSignals.Silence(TimeSpan.FromMilliseconds(300)),
            AudioSignals.Tone(TimeSpan.FromSeconds(4)),
            AudioSignals.Silence(TimeSpan.FromSeconds(1)));

        var utterances = Run(
            Build(new SegmentationOptions { EmitPartials = true, PartialIntervalMs = 1_000 }),
            audio);

        var partials = utterances.Where(u => u.Kind == UtteranceKind.Partial).ToList();
        var final = Assert.Single(utterances, u => u.Kind == UtteranceKind.Final);

        Assert.True(partials.Count >= 2, $"expected repeated partials, got {partials.Count}");
        // Partials and their final share the utterance id, which is how the UI replaces the
        // provisional line instead of duplicating it.
        Assert.All(partials, p => Assert.Equal(final.Id, p.Id));
    }
}

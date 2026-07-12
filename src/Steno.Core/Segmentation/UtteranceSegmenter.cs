using Steno.Core.Abstractions;
using Steno.Core.Audio;

namespace Steno.Core.Segmentation;

/// <summary>
/// Cuts a continuous channel into utterances. This is the component that makes a
/// non-streaming engine (whisper.cpp) feel live — see ADR 0003.
///
/// Fed arbitrary-sized <see cref="AudioFrame"/>s, it re-blocks them to fixed 20 ms
/// frames, runs the VAD, and drives:
///   Idle --(speech for OnsetMs)--> Speaking --(silence for CloseMs | MaxLen)--> emit Final
/// Not thread-safe: one segmenter per channel, fed from that channel's capture thread.
/// </summary>
public sealed class UtteranceSegmenter
{
    private readonly IVoiceActivityDetector _vad;
    private readonly SegmentationOptions _options;

    private readonly List<float> _carry = new(AudioConstants.FrameSamples * 4);
    private readonly Queue<float[]> _preroll = new();
    private readonly List<float> _speech = new(AudioConstants.SampleRate * 8);

    private readonly int _prerollFrames;
    private readonly int _onsetFrames;
    private readonly int _closeFrames;
    private readonly int _partialFrames;
    private readonly int _maxSamples;
    private readonly int _minSamples;

    private int _speechRun;
    private int _silenceRun;
    private int _prerollSamples;
    private int _framesSinceLastPartial;
    private bool _speaking;
    private long _clockSamples = -1;
    private long _utteranceStartSample;
    private Guid _utteranceId;

    public UtteranceSegmenter(IVoiceActivityDetector vad, SegmentationOptions options)
    {
        _vad = vad;
        _options = options;

        _prerollFrames = FramesFor(options.PrerollMs);
        _onsetFrames = Math.Max(1, FramesFor(options.SpeechOnsetMs));
        _closeFrames = Math.Max(1, FramesFor(options.SilenceCloseMs));
        _partialFrames = Math.Max(1, FramesFor(options.PartialIntervalMs));
        _maxSamples = AudioConstants.SamplesForMs(options.MaxUtteranceMs);
        _minSamples = AudioConstants.SamplesForMs(options.MinUtteranceMs);
    }

    /// <summary>Emitted for every Partial snapshot and every Final utterance, in order.</summary>
    public event Action<Utterance>? UtteranceReady;

    public bool IsSpeaking => _speaking;

    public void Push(AudioFrame frame)
    {
        if (_clockSamples < 0)
            _clockSamples = AudioConstants.SamplesFor(frame.Offset);

        _carry.AddRange(frame.Samples);

        var consumed = 0;
        while (_carry.Count - consumed >= AudioConstants.FrameSamples)
        {
            ProcessFrame(CollectionsMarshalSpan(consumed));
            consumed += AudioConstants.FrameSamples;
        }

        if (consumed > 0)
            _carry.RemoveRange(0, consumed);
    }

    /// <summary>Call-ended / capture-stopped: close whatever is open so no speech is lost.</summary>
    public void Flush()
    {
        if (_speaking)
            CloseUtterance();

        _carry.Clear();
        _preroll.Clear();
        _vad.Reset();
        _clockSamples = -1;
    }

    private void ProcessFrame(ReadOnlySpan<float> frame)
    {
        var isSpeech = _vad.IsSpeech(frame);
        _clockSamples += frame.Length;

        if (!_speaking)
        {
            KeepAsPreroll(frame);

            _speechRun = isSpeech ? _speechRun + 1 : 0;
            if (_speechRun >= _onsetFrames)
                OpenUtterance();

            return;
        }

        _speech.AddRange(frame);
        _silenceRun = isSpeech ? 0 : _silenceRun + 1;

        if (_silenceRun >= _closeFrames || _speech.Count >= _maxSamples)
        {
            CloseUtterance();
            return;
        }

        if (!_options.EmitPartials)
            return;

        if (++_framesSinceLastPartial >= _partialFrames)
        {
            _framesSinceLastPartial = 0;
            Emit(UtteranceKind.Partial);
        }
    }

    private void OpenUtterance()
    {
        _speaking = true;
        _speechRun = 0;
        _silenceRun = 0;
        _framesSinceLastPartial = 0;
        _utteranceId = Guid.NewGuid();
        _speech.Clear();

        // Pre-roll first: the VAD needed OnsetMs of evidence, so the utterance's real
        // beginning is already behind us. Without this the first syllable is missing.
        _prerollSamples = 0;
        foreach (var buffered in _preroll)
        {
            _speech.AddRange(buffered);
            _prerollSamples += buffered.Length;
        }

        _preroll.Clear();
        _utteranceStartSample = _clockSamples - _prerollSamples;
    }

    private void CloseUtterance()
    {
        if (SpeechSampleCount() >= _minSamples)
            Emit(UtteranceKind.Final);

        _speaking = false;
        _silenceRun = 0;
        _speechRun = 0;
        _speech.Clear();
    }

    /// <summary>
    /// Voiced samples only — the buffer also holds the pre-roll and the trailing silence that
    /// closed the utterance. Both are kept in the audio (whisper.cpp decodes better with a
    /// little room around the words) but neither counts toward the "was this just a cough?"
    /// test, or a 160 ms click padded with 300 ms of pre-roll would sail past it.
    /// </summary>
    private int SpeechSampleCount() =>
        _speech.Count - _prerollSamples - _silenceRun * AudioConstants.FrameSamples;

    private void Emit(UtteranceKind kind)
    {
        var samples = _speech.ToArray(); // copy: the caller transcribes on another thread
        UtteranceReady?.Invoke(new Utterance(
            _utteranceId,
            kind,
            samples,
            AudioConstants.DurationOf((int)_utteranceStartSample)));
    }

    private void KeepAsPreroll(ReadOnlySpan<float> frame)
    {
        if (_prerollFrames == 0)
            return;

        _preroll.Enqueue(frame.ToArray());
        while (_preroll.Count > _prerollFrames)
            _preroll.Dequeue();
    }

    private ReadOnlySpan<float> CollectionsMarshalSpan(int start) =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_carry)
            .Slice(start, AudioConstants.FrameSamples);

    private static int FramesFor(int milliseconds) => milliseconds / AudioConstants.FrameMilliseconds;
}

using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
using Steno.Core.Segmentation;
using Steno.Core.Session;
using Steno.Core.Transcription;

namespace Steno.Core.Recording;

public interface IRecordingTranscriber
{
    /// <summary>Transcribes a recorded call. A stereo file is read as left = you, right = them.</summary>
    Task<IReadOnlyList<TranscriptEntry>> TranscribeAsync(
        string path,
        SessionOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Offline transcription of a recorded call.
///
/// Reads the recording, splits it back into the two speakers (a stereo file made by
/// <see cref="StereoCallRecorder"/> puts you on the left and them on the right), and runs each
/// channel through the same segmenter and the same policy the live pipeline uses. That reuse is
/// the point: an offline transcript should differ from a live one only by being *better* — it can
/// take its time — never by obeying different rules.
///
/// A mono file has no speaker separation to recover, so it is transcribed as one unnamed speaker.
/// </summary>
public sealed class RecordingTranscriber : IRecordingTranscriber
{
    private readonly ITranscriberFactory _transcriberFactory;
    private readonly ILogger<RecordingTranscriber> _logger;

    public RecordingTranscriber(ITranscriberFactory transcriberFactory, ILogger<RecordingTranscriber> logger)
    {
        _transcriberFactory = transcriberFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TranscriptEntry>> TranscribeAsync(
        string path,
        SessionOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var (local, remote) = ReadChannels(path);
        _logger.LogInformation(
            "Transcribing {Path}: {Seconds:F0}s, {Channels} channel(s)",
            path, (double)local.Length / AudioConstants.SampleRate, remote is null ? 1 : 2);

        // Drafts are meaningless offline — nobody is watching a sentence being formed — and they
        // would double the work for text that is immediately overwritten.
        var transcription = options.Transcription with { EmitPartials = false };
        var segmentation = options.Segmentation with { EmitPartials = false };

        var speakers = remote is null
            ? new ChannelSpeakerResolver("Speaker", "Speaker")
            : new ChannelSpeakerResolver(options.LocalSpeakerName, options.RemoteSpeakerName);

        var utterances = new List<(SpeakerChannel Channel, Utterance Utterance)>();
        CollectUtterances(local, SpeakerChannel.Local, segmentation, utterances);

        if (remote is not null)
            CollectUtterances(remote, SpeakerChannel.Remote, segmentation, utterances);

        // Both speakers on one timeline, in the order the conversation actually happened.
        utterances.Sort((a, b) => a.Utterance.Start.CompareTo(b.Utterance.Start));

        await using var transcriber = await _transcriberFactory
            .CreateAsync(transcription, cancellationToken)
            .ConfigureAwait(false);

        var entries = new List<TranscriptEntry>();

        for (var i = 0; i < utterances.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (channel, utterance) = utterances[i];

            if (TranscriptionPolicy.IsWorthTranscribing(utterance.Samples.Span))
            {
                var result = await transcriber
                    .TranscribeAsync(utterance.Samples, draft: false, cancellationToken)
                    .ConfigureAwait(false);

                if (TranscriptionPolicy.IsUsable(result, transcription.NoSpeechThreshold))
                {
                    var translation = transcription.Translation == TranslationMode.ToEnglish
                        ? await transcriber.TranslateAsync(utterance.Samples, cancellationToken).ConfigureAwait(false)
                        : null;

                    entries.Add(new TranscriptEntry(
                        utterance.Id,
                        channel,
                        speakers.Resolve(channel, utterance),
                        utterance.Start,
                        utterance.End,
                        result.Text,
                        translation?.Text is { Length: > 0 } text ? text : null,
                        result.Confidence,
                        IsFinal: true,
                        result.Tokens));
                }
            }

            progress?.Report((double)(i + 1) / utterances.Count);
        }

        _logger.LogInformation("Transcribed {Path}: {Count} lines", path, entries.Count);
        return entries;
    }

    private static void CollectUtterances(
        float[] samples,
        SpeakerChannel channel,
        SegmentationOptions options,
        List<(SpeakerChannel, Utterance)> into)
    {
        var segmenter = new UtteranceSegmenter(new EnergyVoiceActivityDetector(), options);
        segmenter.UtteranceReady += utterance => into.Add((channel, utterance));

        for (var i = 0; i + AudioConstants.FrameSamples <= samples.Length; i += AudioConstants.FrameSamples)
        {
            into.Capacity = Math.Max(into.Capacity, into.Count);
            segmenter.Push(new AudioFrame(
                samples.AsSpan(i, AudioConstants.FrameSamples).ToArray(),
                AudioConstants.DurationOf(i)));
        }

        // Whatever was still being said when the recording ended.
        segmenter.Flush();
    }

    /// <summary>
    /// Reads any WAV/MP3 the machine can decode, resampled to whisper's 16 kHz. Stereo comes back
    /// as two channels (left, right); mono as one.
    /// </summary>
    private static (float[] Local, float[]? Remote) ReadChannels(string path)
    {
        using var reader = new AudioFileReader(path);

        var channels = reader.WaveFormat.Channels;
        ISampleProvider provider = reader;

        if (reader.WaveFormat.SampleRate != AudioConstants.SampleRate)
            provider = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(
                provider, AudioConstants.SampleRate);

        var interleaved = new List<float>();
        var buffer = new float[AudioConstants.SampleRate * channels];
        int read;

        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            interleaved.AddRange(buffer.AsSpan(0, read).ToArray());

        if (channels == 1)
            return (interleaved.ToArray(), null);

        var frames = interleaved.Count / channels;
        var local = new float[frames];
        var remote = new float[frames];

        for (var i = 0; i < frames; i++)
        {
            local[i] = interleaved[i * channels];
            remote[i] = interleaved[i * channels + 1];
        }

        return (local, remote);
    }
}

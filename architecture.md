# Steno — Architecture

Real-time, per-speaker call transcription (primarily Russian) for Windows desktop.
Audio never leaves the machine: transcription runs locally via [whisper.cpp](https://github.com/ggml-org/whisper.cpp).

---

## 1. Problem shape

A call has two *physically separate* audio streams on the local machine:

| Stream | Source | Who is talking |
|---|---|---|
| **Microphone** | WASAPI capture device | the local user ("Me") |
| **System output** | WASAPI **loopback** on the render device | the remote party/parties |

Because the streams are separate, speaker attribution for a 2-party call is
*free* — no diarization model, no clustering, no error. That is the core bet of
this design: **the channel is the speaker label.**

Diarization is only needed to split *multiple remote participants inside the
loopback stream*. That is deferred (see §7), and the seam for it exists
(`ISpeakerResolver`).

---

## 2. Layers

```
┌──────────────────────────── Steno.App (Avalonia, MVVM) ────────────────────────────┐
│  Styles/Tokens+Controls   the palette; speaker colour is defined once (ADR 0008)    │
│  Views/SetupView          before the call: 3 steps, system defaults pre-filled      │
│  Views/LiveView           during the call: level meters + transcript                │
│  ViewModels/SetupViewModel (what you pick)  MainViewModel (what the session does)   │
│         ↓ uses interfaces only: ITranscriptionSession, ITranscriptExporter          │
└────────────────────────────────────────┬───────────────────────────────────────────┘
                                         │ (interfaces only, no NAudio/Whisper types)
┌────────────────────────────── Steno.Core (netX, headless) ─────────────────────────┐
│                                                                                     │
│  Session      TranscriptionSession ── owns N ChannelPipeline                        │
│                    │                                                                │
│  Pipeline     ChannelPipeline:  IAudioCaptureSource → UtteranceSegmenter →          │
│                                 TranscriptionQueue → ITranscriber → TranscriptEntry │
│                                                                                     │
│  Audio        IAudioCaptureSource, IAudioDeviceProvider, AudioFrame (16k mono f32)  │
│  Segmentation IVoiceActivityDetector, UtteranceSegmenter (VAD state machine)        │
│  Transcription ITranscriber, TranscriptionOptions, TranscriptionResult              │
│  Export       ITranscriptExporter (Markdown / Json / Srt)                           │
└─────────────────────────────────────────────────────────────────────────────────────┘
                                         │ implemented by
┌───────────── Steno.Platform.Windows ───┴──────── Steno.Whisper ────────────────────┐
│  WasapiMicrophoneSource                │  WhisperTranscriber (Whisper.net)          │
│  WasapiLoopbackSource                  │  WhisperModelProvider (download/cache)     │
│  WasapiDeviceProvider                  │                                            │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

`Steno.Core` has **zero** dependency on NAudio, Whisper.net, or Avalonia. It
knows only interfaces + POCOs. Everything platform- or vendor-specific lives
behind a boundary and is wired in `Steno.App/Composition`.

> Physically these are folders inside two projects (`Steno.Core`, `Steno.App`),
> not four assemblies — see [docs/decisions/0007-project-layout.md](docs/decisions/0007-project-layout.md).

---

## 3. Audio path

```
WASAPI device (e.g. 48 kHz, stereo, float/int16)
   → NAudio WasapiCapture / WasapiLoopbackCapture
   → downmix to mono
   → resample to 16 kHz float32          (whisper.cpp only accepts this)
   → AudioFrame { float[] Samples, DateTimeOffset CapturedAt }
   → per-channel pipeline
```

16 kHz mono float32 is the *only* audio format that crosses into `Steno.Core`.
All device-format ugliness is contained in the capture implementations.

---

## 4. Segmentation (how "streaming" is faked)

whisper.cpp is **not** a streaming ASR — it transcribes a finite buffer. Real-time
behaviour is produced by cutting the stream into utterances:

`UtteranceSegmenter` is a state machine fed 20 ms frames:

- **Idle** → energy above threshold for `SpeechStartMs` (≈120 ms) → **Speaking**
- **Speaking** → energy below threshold for `SilenceEndMs` (≈600 ms) → emit **final** utterance
- **Speaking** longer than `MaxUtteranceMs` (≈20 s) → force-cut, emit final
- A `PrerollMs` (≈300 ms) ring buffer is prepended so the first phoneme is not clipped

**Drafts** (on by default) are what make it feel live. Waiting for a sentence to end means a
6-second sentence appears 6 seconds late, however fast inference is. So while Speaking, the
growing buffer is re-transcribed every `PartialIntervalMs` (400 ms) with greedy decoding and
published as a *provisional* entry, replaced by the final one when the sentence closes.

**Perceived latency ≈ 400 ms + inference.** On a GPU that is ~600 ms; on CPU inference alone
is seconds and the drafts are simply dropped (see §5 and ADR 0010).

Re-transcribing a growing buffer is affordable because whisper's cost is *flat* in clip
length — the encoder always processes a padded 30 s window, so a 2 s draft and a 10 s draft
both cost ~170 ms on a GPU.

VAD is energy+ZCR based (`EnergyVoiceActivityDetector`) — cheap, no model, good
enough on a headset. `IVoiceActivityDetector` allows swapping in Silero later.

---

## 5. Transcription

- **Backend: Vulkan GPU, falling back to CPU.** The single most important number in this
  document: large-v3-turbo takes **~170 ms on a GPU and ~11,000 ms on a 16-core CPU**. Live
  transcription is a GPU feature. CUDA is deliberately not used — its build crashes the
  process on Blackwell cards (ADR 0010).
- Binding: **Whisper.net** (managed wrapper over whisper.cpp, ships native runtimes).
- **One `WhisperProcessor` per channel.** whisper.cpp contexts are not thread-safe;
  per-channel processors let Mic and Remote transcribe in parallel and keep each
  channel's decoding context (prompt continuity) clean.
- Model: GGML file (`ggml-large-v3-turbo` recommended, `small`/`medium` for weak CPUs),
  downloaded once to `%LOCALAPPDATA%/Steno/models`.
- Language: forced to `ru` by default (auto-detect optional) — forcing avoids the
  language flapping that wrecks short utterances.
- **Translation**: whisper's `translate` task only ever targets English. So:
  - `TranslationMode.Off` — Russian transcript only.
  - `TranslationMode.WhisperToEnglish` — a *second* whisper pass over the same
    utterance with `Translate=true`, producing `Entry.Translation`.
  - `ITranslator` seam exists for a future non-English / LLM translator.
- Back-pressure: a bounded channel per pipeline. If whisper falls behind, the
  oldest *partial* jobs are dropped first; finals are never dropped.

---

## 6. Session & transcript model

```csharp
record TranscriptEntry(
    Guid Id, SpeakerChannel Channel, string Speaker,
    TimeSpan Start, TimeSpan End,
    string Text, string? Translation,
    float Confidence, bool IsFinal);
```

`TranscriptionSession` owns the channel pipelines, a monotonic session clock
(entries are timestamped against session start, so the two channels interleave
correctly), and an observable transcript store the UI binds to. Entries arrive
out of order across channels; the store inserts by `Start`.

---

## 7. Deliberately deferred

| Thing | Why deferred | Seam that exists |
|---|---|---|
| Diarization of 3+ remote speakers | 2-party is the stated case; channel split already solves it | `ISpeakerResolver` |
| Acoustic echo cancellation | headset use is assumed; speakers cause mic to re-hear the remote party | capture layer |
| Silero VAD | energy VAD is adequate on a headset | `IVoiceActivityDetector` |
| Non-English translation | whisper can only translate → English | `ITranslator` |
| Linux/macOS capture | Windows-first (WASAPI loopback) | `IAudioCaptureSource` |

Each is a swap of one implementation, not a rewrite.

## 8. Known limitations

- **Speaker playback re-entry**: with loudspeakers, the mic hears the remote party →
  duplicated text attributed to "Me". Use a headset. AEC is the real fix.
- whisper.cpp hallucinates on pure silence/noise → VAD gating plus a
  `MinUtteranceMs` floor and a no-speech-probability filter.
- CPU: `large-v3-turbo` on 2 parallel channels wants a modern 8-core or a GPU
  runtime (`Whisper.net.Runtime.Cuda` / `.Vulkan`).

## 9. Rules for contributors

- Max **300 lines** per file. Past that, decompose.
- `Steno.Core` may not reference NAudio / Whisper.net / Avalonia.
- Every non-obvious implementation decision gets an ADR in [docs/decisions/](docs/decisions/).

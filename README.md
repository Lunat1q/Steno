# Steno

Live, per-speaker call transcription for Windows. Primarily Russian. Fully local —
audio never leaves the machine, transcription runs on
[ggml-org/whisper.cpp](https://github.com/ggml-org/whisper.cpp).

Turn it on before a call. It listens to **two streams at once**:

- your **microphone** → labelled *Me*
- the **system output**, captured via WASAPI loopback → labelled *Remote* (works with
  Teams, Zoom, a browser, any softphone — no per-app integration)

Because the streams are physically separate, who-said-what is exact. No diarization
model, no guessing (see [ADR 0002](docs/decisions/0002-per-channel-speaker-attribution.md)).

## Run it

```
dotnet run --project src/Steno.App
```

Your default microphone and speakers are already selected, so the short version is: open
it, press **Start listening**. The first run downloads the speech model to
`%LOCALAPPDATA%/Steno/models` (Balanced ≈ 1.5 GB, once); choose **Fast** on a weak laptop.

**Latency is a GPU question.** Measured end to end on a GPU (Vulkan — NVIDIA, AMD or Intel,
nothing to install): **~700 ms** from speaking a word to seeing it, and **~760 ms** for the
corrected final line after you stop talking. On CPU the same model takes ~11 seconds per
utterance, so the app says so in a banner and you should pick **Fast**. The live header always
shows which backend is in use. Numbers and reasoning:
[ADR 0010](docs/decisions/0010-sub-second-latency.md) and
[ADR 0014](docs/decisions/0014-cold-start-and-capture-latency.md).

During the call you get two level meters — **You** in sky, **Them** in amber — so a muted
mic or misrouted call audio is obvious immediately, and a transcript in those same two
colours. Translation and live drafts sit under **More options**; English is the only
language whisper can translate into ([ADR 0005](docs/decisions/0005-translation.md)).

> **Use a headset.** On loudspeakers the microphone hears the remote party and their
> words get attributed to you. There is a mitigation (`Suppress speaker echo`) but it is
> a heuristic, not echo cancellation — [ADR 0006](docs/decisions/0006-echo-and-crosstalk.md).

## Layout

| Path | What |
|---|---|
| [architecture.md](architecture.md) | how it fits together — read this first |
| [docs/decisions/](docs/decisions/) | why it is built this way |
| [src/Steno.Core/](src/Steno.Core/) | audio capture, VAD segmentation, whisper.cpp, session |
| [src/Steno.App/](src/Steno.App/) | Avalonia UI |
| [tests/](tests/) | segmenter and echo-gate tests |

```
dotnet test
```

## Conventions

Max 300 lines per file. `Steno.Core` outside `Platform/` and `Whisper/` must not
reference NAudio or Whisper.net. Every non-obvious decision gets an ADR.

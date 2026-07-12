# ADR 0001 — Transcription engine: whisper.cpp, accessed via Whisper.net

**Status:** accepted · **Date:** 2026-07-12

## Context

The engine must be [ggml-org/whisper.cpp](https://github.com/ggml-org/whisper.cpp) — the
C/C++ ggml inference port. Explicitly **not** OpenAI's Python `whisper` package, not
`faster-whisper`/CTranslate2, and not the hosted OpenAI audio API. Everything runs
locally; call audio never leaves the machine.

That leaves only the question of *how a .NET process calls into it*.

## Options

1. **`Whisper.net` (NuGet)** — a managed binding whose native payload
   (`Whisper.net.Runtime`) **is whisper.cpp compiled from ggml-org/whisper.cpp**.
   It P/Invokes `whisper_init_from_file`, `whisper_full`, etc., loads the same
   `ggml-*.bin` GGML models, and ships CPU / CUDA / Vulkan / CoreML runtime variants.
2. **Hand-written P/Invoke** over a `whisper.dll` we build from the repo ourselves.
3. **Shell out to `whisper-cli.exe`** (the repo's example binary) per utterance.

## Decision

**Option 1.** Whisper.net is not a different engine — it is whisper.cpp plus the
`DllImport` layer we would otherwise write by hand, with the native builds already
produced for win-x64/arm64 + GPU backends.

Option 3 is rejected: process spawn per utterance (~50–150 ms) plus a model *reload*
per call destroys the real-time budget. The model must stay resident.

Option 2 stays viable and is cheap to switch to, because the codebase talks to
`ITranscriber` ([src/Steno.Core/Transcription/ITranscriber.cs](../../src/Steno.Core/Transcription/ITranscriber.cs)),
never to Whisper.net types. Take it if we need a whisper.cpp feature the binding
lags on (e.g. a new sampling flag, `whisper_state` reuse, tinydiarize `tdrz`), or a
custom-built native lib. `WhisperTranscriber` is the only file that would change.

## Consequences

- Models are GGML (`ggml-large-v3-turbo.bin`, `ggml-medium.bin`, …), from the
  whisper.cpp model set — the only format whisper.cpp accepts.
- Native runtime is chosen by NuGet package: `Whisper.net.Runtime` (CPU, default),
  `.Runtime.Cuda`, `.Runtime.Vulkan`. Swappable without touching code.
- We inherit whisper.cpp's constraint set: **16 kHz mono float32 PCM input**, no
  true streaming, `translate` task targets English only. These shape §4/§5 of
  [architecture.md](../../architecture.md).

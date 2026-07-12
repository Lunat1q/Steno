# ADR 0014 — The lag was cold start, not the model

**Status:** accepted · **Date:** 2026-07-12 · **Refines:** [ADR 0010](0010-sub-second-latency.md)

## Context

Even on the GPU, with drafts enabled, the app still *felt* laggy. ADR 0010 predicted
~600 ms from theory. Theory was wrong, so I measured it instead: a harness that feeds real
speech (`jfk.wav`) through the real `ChannelPipeline` **at wall-clock speed**, timestamping
when text actually surfaces relative to when the words were spoken.

## What the measurement showed

Whisper was never the problem. Drafts ran on schedule (every 400 ms, ~170 ms each). But:

| | before | after |
|---|---|---|
| first draft, after speech starts | **4,867 ms** (cold run) | **705 ms** |
| final, after speech stops | 3,201 ms* | **760 ms** |

\* *that one was the harness lying to me — see below.*

### 1. The real bug: the first inference on a GPU is enormously slow

Vulkan compiles its compute pipelines on first use. The first `whisper_full` call therefore
takes seconds, not milliseconds — and the pipeline's back-pressure rule ("drop drafts when the
queue is backing up") then **discarded every draft that piled up behind it**. The opening
seconds of every call produced nothing at all, which is exactly what "still laggy" felt like.

Between two runs of the same harness, the first draft took 4,867 ms (cold) and 770 ms (warm).
Same code. That gap *was* the complaint.

**Decision.** `WhisperTranscriber.WarmUpAsync` pushes one second of silence through every
processor immediately after the model loads, inside `ITranscriberFactory.CreateAsync`. The cost
is paid while the UI already says "Warming up…" and nobody is speaking. It is not an
optimisation; it moves an unavoidable cost out of the user's first sentence.

### 2. Two latency fees worth refunding

- **WASAPI capture buffer: 100 ms → 40 ms**, with event-sync so the device wakes us instead of
  being polled. NAudio's default is pure latency before a word even reaches the VAD.
- **VAD onset: 120 ms → 80 ms.** The pre-roll buffer means the audio is never lost, only the
  moment we start paying attention — so this is cheap to shorten.

### 3. A warning about measurement harnesses

My first harness stopped feeding audio the instant the speech ended, so the segmenter never saw
the silence that *closes* an utterance — and the "final" only appeared when the test tore the
pipeline down. It reported 3,201 ms of latency that did not exist. A real microphone keeps
delivering silence, so the harness must too.

Do not trust a latency number from a harness that does not keep the stream running.

## Consequences

- Both channels share one GPU. A draft costs ~170 ms on `Balanced`, so two people talking at
  once is ~340 ms of GPU work per 400 ms window — tight but fine. On **Best** (~280 ms/draft)
  simultaneous speech saturates the GPU and drafts start getting dropped. **Balanced is the
  right default**, and Best is for accuracy over liveness.
- Startup is slightly slower (one extra silent inference per processor, ~200 ms on GPU) in
  exchange for the first sentence of the call actually appearing.

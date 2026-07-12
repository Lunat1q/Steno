# ADR 0010 — Sub-second latency: GPU first, then drafts

**Status:** accepted · **Date:** 2026-07-12

## The problem

First real use: text was correct but arrived many seconds late. Two causes, one of them
dominating everything else.

## Measured, not guessed

whisper.cpp on `jfk.wav` (11 s of speech), sliced to the buffer sizes a live pipeline
actually sees. Machine: Ryzen 9 9950X3D (16 cores), RTX 5090.

| backend | small | large-v3-turbo | large-v3 |
|---|---|---|---|
| **CPU** | ~2,200 ms | **~11,100 ms** | ~12,300 ms |
| **Vulkan (GPU)** | ~150 ms | **~170 ms** | ~280 ms |

Two facts fall out of this table, and they drive the whole design:

1. **The GPU is worth ~65×.** No amount of VAD tuning competes with that. Sub-second
   transcription is a GPU feature, not a tuning feature.
2. **Inference cost barely depends on clip length.** 1.5 s and 11 s of audio cost the same,
   because whisper's encoder always processes a *padded 30-second window*. So transcribing a
   growing buffer repeatedly is not the quadratic disaster it looks like — each draft costs a
   flat ~170 ms — and there is no point capping the draft window.

## Decisions

### 1. Vulkan, not CUDA

`Whisper.net.Runtime.Vulkan`, with `RuntimeLibraryOrder = [Vulkan, Cpu]` — deliberately
*not* Whisper.net's default order, which tries CUDA first. The CUDA build ships no kernels
for Blackwell (sm_120) and **takes the process down** rather than throwing something
catchable — verified here, exit code 9, no managed exception. Vulkan covers NVIDIA, AMD and
Intel from one package and falls back to CPU cleanly.

### 2. Drafts while speaking, on by default

Previously the transcript only appeared *after* the speaker stopped — a 6-second sentence
surfaced 6+ seconds late, no matter how fast inference was. That is structural: a whisper
pass needs a finished buffer.

So the buffer is transcribed *while it grows*: every `PartialIntervalMs` (400 ms) the
in-progress utterance is decoded and shown as provisional text, replaced by the final when
the sentence closes.

**Perceived latency ≈ 400 ms (draft interval) + ~200 ms (inference) ≈ 600 ms.** Under a
second, which was the ask.

Drafts use a greedy sampling strategy with no temperature fallbacks — the text lives for
under a second before the final overwrites it, so decode accuracy there buys nothing.

### 3. Silence-close 600 ms → 400 ms

Cuts final latency by 200 ms. This is roughly the floor: below it, natural mid-sentence
pauses start cutting sentences in half, and whisper loses more accuracy from the truncation
than the latency is worth.

## Consequences

- **On CPU the app cannot be live**, and pretending otherwise would be a lie. Drafts stay
  enabled (the pipeline already drops the ones it cannot keep up with, so they cost nothing),
  the backend is displayed permanently in the live header, and a CPU fallback raises a banner
  telling the user to pick **Fast**. On CPU with **Fast**, ~2.2 s per utterance is the honest
  ceiling.
- GPU memory: two channels × (main + draft + optional translate) processors share one loaded
  model, so VRAM is roughly one model, not six.
- `large-v3` ("Best") is now viable live on a strong GPU (~280 ms). It was the *worst*
  possible choice on CPU (~12 s), which is exactly what the first user hit.

# ADR 0003 — Faking real-time: VAD segmentation, not streaming

**Status:** accepted · **Date:** 2026-07-12

## Context

whisper.cpp transcribes a *finite buffer*. It has no streaming/incremental decoder.
The app must nonetheless show text "instantly" during a live call.

## Decision

Cut each channel's stream into **utterances** with a VAD state machine
(`UtteranceSegmenter`) and transcribe each one as it closes.

| Knob | Default | Reason |
|---|---|---|
| Frame size | 20 ms | VAD granularity |
| Speech onset | 120 ms above threshold | ignores clicks/keystrokes |
| Silence to close | 600 ms below threshold | shorter cuts mid-sentence; longer feels laggy |
| Pre-roll | 300 ms | prepended so the first phoneme is not clipped |
| Max utterance | 20 s | force-cut for monologues; bounds worst-case latency |
| Min utterance | 250 ms | below this it is a cough — dropped, whisper hallucinates on it |

Perceived latency ≈ `silence-to-close (600 ms) + inference time`.

**Partial results** (optional): while speech is ongoing, re-transcribe the growing
buffer every ~1.2 s at reduced effort (greedy, no fallback) and publish a
provisional entry that the final one replaces. Roughly doubles CPU, so it is a
toggle, off by default on weak machines.

## Alternatives rejected

- **Fixed-size chunking (e.g. every 3 s)** — cuts words in half; whisper's accuracy
  collapses at arbitrary boundaries and cross-chunk context stitching is a mess.
- **whisper.cpp's `stream` example (sliding window + token-level dedup)** — its
  duplicate/overlap heuristics are exactly the fragility VAD gating avoids, and it
  assumes one continuous speaker, which we don't have.

## Consequences

- A speaker who never pauses gets text only every 20 s (mitigated by partials).
- Energy VAD is dumb about background noise; `IVoiceActivityDetector` allows
  dropping in Silero VAD if a noisy environment demands it.
- whisper's own `no_speech_prob` is used as a second filter, since VAD gating alone
  still lets breath/noise through and whisper answers with confident garbage.

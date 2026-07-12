# ADR 0002 — Speaker attribution by audio channel, not diarization

**Status:** accepted · **Date:** 2026-07-12

## Context

Calls are mostly 2-party. Who said what must be known per utterance.

## Decision

**The channel is the speaker label.** The local user is whatever the microphone
capture hears; the remote party is whatever the system render device plays
(WASAPI loopback). Two independent pipelines, two labels, zero inference.

A diarization model (clustering speaker embeddings) is *not* run.

## Why

- 100% accurate for 2-party, versus ~85–95% for diarization, and it costs nothing.
- No extra model, no extra latency, no speaker-count guessing.
- It also improves the ASR itself: each pipeline VADs and segments one voice, so
  utterance boundaries are clean and whisper never has to cut across a turn change.

## Consequences

- **3+ remote participants collapse into one label** ("Remote"). Splitting them
  requires diarization *inside* the loopback stream. Deferred; the seam is
  `ISpeakerResolver`, which today returns a constant per channel and can later
  return a clustered speaker id.
- **Loudspeakers break the assumption**: the mic re-hears the remote party, so their
  words appear twice, once mislabelled "Me". A headset is assumed. The real fix is
  acoustic echo cancellation (feed the loopback signal as the AEC reference) — see
  ADR 0006.
- The two pipelines share a session clock so their entries interleave in true time
  order despite being transcribed independently and out of order.

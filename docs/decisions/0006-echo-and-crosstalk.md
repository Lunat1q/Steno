# ADR 0006 — Echo/cross-talk: assume a headset, defer AEC

**Status:** accepted · **Date:** 2026-07-12

## The problem

With **loudspeakers**, the microphone hears the remote party coming out of the
speakers. Both pipelines then transcribe the same words, and the mic copy is
labelled "Me". The transcript lies about who said what — the one failure mode that
destroys the app's entire value proposition (ADR 0002).

With a **headset** the problem does not exist: the mic cannot hear the earcups.

## Decision (v1)

Assume a headset. Warn in the UI when the selected render device is not a headset-ish
endpoint, and ship a cheap mitigation instead of real AEC:

**Cross-talk gate** — a mic utterance is suppressed if, over the same time window,
the loopback channel had significantly higher energy and the mic energy tracks it.
Implemented as a comparison of the two channels' energy envelopes, in
`CrossTalkGate`. It is a heuristic, deliberately conservative: it prefers to let a
duplicate through rather than delete real speech from the user.

## Deferred: real AEC

Proper fix is an adaptive echo canceller with the loopback stream as the reference
signal (WebRTC APM / Speex AEC). It removes the echo from the mic *signal* instead
of discarding the *utterance*, and it also fixes the case where both parties talk at
once. It needs sample-accurate alignment between the two WASAPI streams (they have
independent clocks and drift), which is real work — hence deferred, not dismissed.

Until then: **use a headset.** This is stated in the README and the UI.

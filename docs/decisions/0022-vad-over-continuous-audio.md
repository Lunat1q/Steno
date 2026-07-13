# ADR 0022 — The VAD could not hear a pause that was not silent

**Status:** accepted · **Date:** 2026-07-12 · **Amends:** [ADR 0015](0015-vad-deafness.md)

## Symptom

A film playing through the loopback channel produced **no transcript at all**, while the level
meter for that channel danced happily. A YouTube talking-head video worked, but "congested" —
whole paragraphs arrived as one block instead of sentence by sentence.

Two symptoms, one cause. The film was the extreme case of what YouTube only hinted at.

## Cause

`EnergyVoiceActivityDetector` estimated its noise floor with an EMA that, per ADR 0015, was
allowed to learn **only from frames it had already judged quiet** (`if (!loud) Adapt(rms);`).

That rule is sound on a headset, where the pauses between words really are near-silent. It is
circular over film audio, where they are not. Dialogue in a film rides on a continuous bed of
score, room tone and effects — measured on the sample below, median frame RMS **0.042**, and
the quietest tenth still at 0.0036. The floor started at 1×10⁻⁴, which put the speech threshold
at 4×10⁻⁴ — *a hundred times below the music*. So:

- every frame was "loud", so the floor never got a frame it was allowed to learn from;
- the floor stayed pinned at its minimum forever;
- **99% of frames were "loud" and 93% passed as speech** — including the pauses;
- the segmenter therefore never saw `SilenceCloseMs` of silence, and never closed an utterance
  on a pause. It only ever force-cut at `MaxUtteranceMs`.

Measured over 120 s of the real file, the old detector emitted **six utterances, every one of
them a 20 s `MAXLEN` brick**. Each is a 20 s whisper job arriving every 20 s: the queue never
catches up, and what does arrive is a wall of text. Hence "nothing" on the film, and "too many
sentences in one block" on YouTube.

The floor could not see a background it never got below.

## Decision

**The noise floor is the 10th percentile of the last 3 seconds of audio**, clamped to
`[1e-6, MaxNoiseFloor]`.

```csharp
_recent[_next] = rms;                       // ring buffer, 150 × 20 ms frames
_noiseFloor = Percentile(_recent, 0.10f);   // clamped to MaxNoiseFloor
var loud = rms > AbsoluteFloor && rms > _noiseFloor * SnrFactor;
return loud && IsVocalZeroCrossingRate(frame);
```

A percentile does not need permission to observe the background — it just looks at the quiet
end of the window. The pauses between sentences are still in there. They are simply not silent,
and a percentile measures "quietest tenth", not "silence".

ADR 0015's actual invariant is **preserved and strengthened**: the ZCR veto still never feeds
back into the floor, because the floor is computed from energy alone, before any classification
happens. There is no longer a classification for it to learn from at all — which is the real
answer to that ADR's lesson.

Two constants moved with it:

- `SnrFactor` 4.0 → **3.0** (~10 dB). Dialogue mixed over music does not get 12 dB of headroom.
- `MaxNoiseFloor` 0.02 → **0.015**. With the lower SNR factor this caps the speech threshold at
  0.045, still below even a quiet talker (~0.05), so the ADR 0015 deafness guarantee holds:
  a fully saturated floor cannot silence a human voice.

## Result

Same 120 s of film audio, through the real segmenter:

| | utterances | durations |
|---|---|---|
| before | 6 | 20.0 s × 6 — all force-cut, none closed by a pause |
| after | 24 | 1.2 s – 11.8 s — sentences |

`VadStabilityTests.Dialogue_over_a_continuous_music_bed_still_breaks_into_sentences` pins it:
speech over a bed that never stops must still close on pauses, and no utterance may hit
`MaxUtteranceMs`. The two ADR 0015 deafness tests still pass unchanged.

## Lesson

ADR 0015 removed a feedback loop by restricting what the floor was allowed to learn from. The
restriction was itself a classification — "quiet" — made by the thing being calibrated, and it
inherited the same blind spot one level down: a detector that only learns from frames it already
believes are background cannot discover a background it has never been below.

A statistic over raw input has no such opinion. When a heuristic must be calibrated, calibrate it
against the signal, not against its own verdicts.

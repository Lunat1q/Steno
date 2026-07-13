# ADR 0023 — Capture level is not speech level

**Status:** accepted · **Date:** 2026-07-12 · **Amends:** [ADR 0019](0019-hallucinated-subtitles.md)

## Symptom

With [ADR 0022](0022-vad-over-continuous-audio.md) in place, a film played over the loopback
channel still produced almost nothing: **four words in three minutes, and only when a character
screamed.** The level meter moved the whole time.

## Cause

Not the VAD this time. The audio the app *receives* is not the audio in the file.

Captured through the real WASAPI loopback path — a SteelSeries Sonar virtual endpoint at 70%
volume — the same 90 seconds of film measured:

| | p10 | p50 | p90 | max |
|---|---|---|---|---|
| the file on disk | 0.0142 | 0.0425 | 0.1133 | 0.159 |
| what loopback delivered | 0.0002 | 0.0018 | 0.0089 | 0.017 |

**An order of magnitude quieter.** Loopback captures the render mix *after* the volume slider and
after whatever the endpoint's own processing does to it. The user's volume setting is not a
property of the speech.

Segmentation survived this — the VAD's floor is relative, so it still found utterances covering
**16 of the 16 subtitle lines** in that window. What killed the transcript was the gate one step
later, `TranscriptionPolicy.MinSpeechRms = 0.004`, chosen in ADR 0019 on the reasoning that
"speech sits at 0.05–0.3 even from a quiet talker". Against the real captured stream, utterance
RMS ran **0.0012–0.008**. So:

```
 12 of 24 utterances GATED by MinSpeechRms — every one of them real dialogue
```

They were the *quiet* half of the scene. What survived the gate was shouting. Exactly the reported
symptom, and the gate did it before whisper ever ran.

Whisper compounded it: it does not normalise its input and is measurably worse on very quiet
audio. Handed the same utterances at capture level it returned lower confidence and occasional
empty results; handed them at speech level it returned the subtitle text nearly verbatim.

## Decision

**1. The energy gate is a silence gate, not a speech gate.** `MinSpeechRms` drops from `0.004` to
`3e-4` — digital silence, the same absolute floor the VAD uses. Whether an utterance is *speech*
was already decided, relative to the channel's own noise floor, by the VAD. The gate's remaining
job is only ADR 0019's: keep whisper away from *nothing at all*, because it answers silence with
invented subtitles. That job is unaffected by the volume slider; the old one was entirely at its
mercy.

**2. Whisper is handed the utterance at the level it expects.** `TranscriptionPolicy.Normalize`
scales each utterance to ~0.08 RMS, capped at 30×, clamped against clipping. Shared by the live
and offline paths, like every other rule in that class.

The cap is the safety catch: room tone amplified 100× is what whisper hallucinates over, so a
near-silent channel stays near-silent. The gate runs on the audio **as captured**, before any
gain — amplified hiss must never sail through it.

## Result

Same 92 seconds, captured through the real loopback path, run through the real pipeline:

| | lines reaching the transcript |
|---|---|
| before | 10 of 24 (12 gated as "silence", all of them dialogue) |
| after | 21 of 24, matching the film's own subtitles nearly verbatim |

Per-word confidence rose with the gain, too — the same line came back at 0.73 unnormalised and
0.92 normalised, which matters because confidence drives the transcript's shading (ADR 0013).

## Lesson

ADR 0019 calibrated a threshold against what speech sounds like *in a file*. Two capture paths
later it was defending against a level the app never actually sees. An absolute threshold on a
signal whose gain is controlled by a slider the user can drag is not a threshold — it is a
coin toss with a volume knob attached.

Measure the number on the stream the app really gets, not the one the format specifies.

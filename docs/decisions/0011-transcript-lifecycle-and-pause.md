# ADR 0011 — The transcript survives Stop; Pause is a privacy control

**Status:** accepted · **Date:** 2026-07-12

## 1. Stop must not destroy the transcript

**The bug.** The screen switched on `IsRunning`, so pressing **Stop** returned the user to the
setup card and the entire transcript vanished — the app's only work product, gone, with no
warning and no undo. It was never written anywhere: entries live in memory until exported.

**Decision.** Three screen states, not two:

| State | When | What it offers |
|---|---|---|
| Setup | no session, no transcript | pick devices, Start |
| Live | session running or paused | meters, Pause, Stop |
| **Review** | session stopped, transcript present | the transcript, and **Save** |

The transcript stays on screen after Stop. `HasUnsavedTranscript` is set by any final entry
and cleared **only** by a successful export. While it is set, every path that would destroy
the transcript — *Start a new call*, *close the window* — is intercepted and the user is asked
first, with **Save first / Erase it / Cancel**.

Confirmation is an inline banner, not a modal dialog: no dependency, nothing to dismiss
blindly, and the text being discussed stays visible behind it.

## 2. Save must look like a button you can press

It was styled `quiet` — outlined, grey, muted — which reads as *disabled*. Users do not click
buttons that look read-only, and this is the button that turns a call into an artifact.

**Decision.** `Button.save` is a filled control in the "You" sky colour, and disabled buttons
now get an explicit 0.4 opacity so that *deliberately* disabled and *merely quiet* are no
longer the same visual state.

## 3. Pause suspends transcription, not capture

For a break, or a stretch of the conversation that must not be recorded.

**Decision.** `ITranscriptionSession.SetPaused(bool)` flips `ChannelPipeline.IsPaused`, which
drops audio **before the segmenter** — so paused audio never reaches whisper.cpp and cannot
appear in a transcript. Pausing also flushes the sentence in flight, so a half-finished
sentence is not glued to whatever gets said after the break.

What deliberately keeps running:

- **The capture devices**, so resuming is instant (re-opening WASAPI takes hundreds of ms).
- **The session clock**, so timestamps after the break still match the real call.
- **The level meters**, because the point of a pause indicator is to prove the app is alive
  and *not listening* — a frozen meter is indistinguishable from a crash.

`PausedPipelineTests` asserts on what the **transcriber** received, not on what the UI shows:
a pause that merely hid the text while still sending audio to whisper would be a privacy bug,
and only a test at that layer can tell the difference.

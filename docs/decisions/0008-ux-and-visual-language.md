# ADR 0008 — UX: two screens, two colours, no jargon

**Status:** accepted · **Date:** 2026-07-12

## Context

The first UI exposed the engine: a row of dropdowns labelled *Model*, *Language*,
*Translate → English*, *Live partials*, *Suppress cross-talk*, all visible at once, all
meaningless to someone who is about to take a call and has thirty seconds.

## Decisions

### One screen per moment

`Setup` (before) and `Live` (during) are separate views, never both. Before a call you
choose two devices; during a call you read a transcript. Nothing from one phase clutters
the other.

### The speaker *is* a colour

**You = sky (`#38BDF8`), Them = amber (`#FBA94C`)**, applied identically to the meter,
the name tag and the bubble; your own words sit on the right like every messaging app.
Blue/amber is the colour-blind-safe pair — this matters more than usual, because the
colour carries the app's core claim (who said what, ADR 0002). Everything else on screen
is grey: a transcript is a reading surface.

### Live meters, not a status string

Two level bars, fed by `ITranscriptionSession.LevelChanged` (~10 Hz, smoothed, sqrt-scaled
because speech RMS is 0.05–0.3 and a linear bar reads as permanently empty). A muted mic
or call audio routed to the wrong endpoint is then visible in five seconds, instead of
discovered as an empty transcript after the call. This is the single highest-value UI
element in the app.

### Names the user's world uses, not the engine's

| Was | Is |
|---|---|
| Model: `large-v3-turbo` | Quality: **Balanced** — "Accurate, keeps up in real time" |
| Language: `ru` (text box) | Language: **Russian** (list) |
| "Live partials" | "Show words as they are spoken" |
| "Suppress cross-talk" | "Ignore the other person leaking into my microphone" |

`QualityChoice` maps the human axis (fast ↔ accurate) onto `WhisperModel`. The whisper.cpp
model name never reaches the screen.

### Self-explanatory beats documented

Every control that needs explaining carries one line of plain text *under* it, not a
tooltip nobody hovers. The setup card is three numbered steps, pre-filled with the system
default devices, so the honest zero-configuration path is: open, press **Start listening**.

Two things are stated on screen because getting them wrong ruins the output:

- **Wear a headset** — a permanent banner, not a footnote. Loudspeaker echo puts the other
  person's words under your name (ADR 0006).
- **The model downloads once (~1.5 GB)** — announced *before* Start, not discovered after
  pressing it.

### Errors are instructions

`MainViewModel.Explain` maps the three failures users actually hit (mic permission denied,
model download failed, no device selected) to a sentence with a next action. Everything
else falls back to the raw message. No stack traces on screen.

## Consequences

- Advanced settings (translation, live drafts, echo gate) live behind **More options**.
  They are still one click away; they are just not the first thing a new user reads.
- The colour mapping is now load-bearing. Changing `You`/`Them` in `Styles/Tokens.axaml`
  changes it everywhere — which is the point — but do not split them across views.
- Level metering adds a small per-frame cost (an RMS already computed for the VAD) and a
  ~10 Hz UI update per channel. Negligible against inference.

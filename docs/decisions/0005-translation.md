# ADR 0005 — Translation is a second whisper pass, and only ever to English

**Status:** accepted · **Date:** 2026-07-12

## Context

Requirement: "do translation instantly". Primary call language is Russian.

## Constraint (whisper.cpp, not a design choice)

whisper has two tasks: `transcribe` (text in the source language) and `translate`
(**source → English, and nothing else**). There is no ru→de, no en→ru. The model
was never trained for it.

## Decision

`TranslationMode`:

- `Off` — Russian transcript only. Default.
- `ToEnglish` — after the final Russian transcript for an utterance, run **a second
  whisper pass over the same audio buffer** with `Translate = true`, filling
  `TranscriptEntry.Translation`. The UI shows both lines.

Two passes, not one, because a single pass gives you *either* the Russian text *or*
the English — never both, and the Russian transcript is the primary artifact.

The second pass is queued at lower priority than any pending transcription: live
transcription must never stall waiting on a translation.

## Consequences

- `ToEnglish` roughly doubles inference cost per utterance. On a weak CPU it will
  fall behind; back-pressure drops translations, never transcripts.
- **Any other target language needs a different engine.** `ITranslator` is the seam —
  a local LLM (e.g. via llama.cpp) or a cloud API would implement it. Note that a
  cloud translator would send call text off the machine, which contradicts the
  local-only posture; that is a decision for whoever enables it.

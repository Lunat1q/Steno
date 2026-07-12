# ADR 0019 — "Продолжение следует…": whisper hallucinating on silence

**Status:** accepted · **Date:** 2026-07-12

## Symptom

"Продолжение следует..." ("to be continued") appeared in transcripts out of nowhere, mostly
during noise rather than speech. Its relatives show up too: "Спасибо за просмотр!", subtitle
credits like "Субтитры сделал DimaTorzok", "[Музыка]".

## Cause

Whisper was trained on YouTube subtitles. During quiet passages, those subtitle tracks contain
exactly this furniture — end cards, credits, "thanks for watching" — so when the model is handed
noise or silence, that is what it produces. It is the single best-known Whisper failure mode, and
in Russian "Продолжение следует..." is overwhelmingly its favourite.

## Why the existing filter never had a chance

`TranscriptionOptions.NoSpeechThreshold` was supposed to catch this. Measured against
`large-v3-turbo`:

| input | no-speech prob | confidence | text |
|---|---|---|---|
| **pure silence** | **0.000** | 0.85 | "Продолжение следует..." |
| quiet hiss | 0.000 | 0.83 | "Продолжение следует..." |
| room noise | 0.000 | 0.82 | "Продолжение следует..." |
| keyboard clicks | 0.000 | 0.82 | "Продолжение следует..." |

Given *pure digital silence*, the model reports a **0.000 probability that this was not speech**,
with 85% token confidence. It is not uncertain. It is confidently wrong.

No threshold — on no-speech probability, on confidence, on entropy — can separate that from real
speech, because on those axes it *is* real speech. **The content is the only signal.**

This is worth remembering beyond this bug: a model's confidence is not evidence. It tells you how
sure the model is, not whether it is right, and a model can be perfectly sure about something it
invented wholesale.

## Decision

Two filters, cheapest first, in `ChannelPipeline`:

1. **An energy gate before inference.** An utterance quieter than RMS 0.004 is not a sentence.
   Speech sits at 0.05–0.3 even from a quiet talker, so the margin is wide. This keeps silence
   away from whisper entirely, and saves a GPU pass.
2. **`HallucinationFilter` on the output.** A whole-line match against the subtitle boilerplate
   whisper invents (plus a regex for subtitle credits). Whole-line only: a caller who genuinely
   says "продолжение следует" *inside a sentence* keeps it — it is the utterance consisting of
   nothing else that is fake.

Verified end to end: hiss, room noise and silence pushed through the real pipeline with the real
model now produce **zero** transcript entries.

## Consequences

- Someone whose entire utterance is "Спасибо за просмотр" — nothing else, nothing around it —
  loses that line. This is the right trade: the phrase is near-worthless as dialogue and
  near-certain as a hallucination.
- The blocklist is language-specific and will need additions as new phrases surface. It lives in
  one file, and adding a phrase is one line.
- `NoSpeechThreshold` is kept as a secondary filter but is now known to be near-useless for this;
  the energy gate and the content filter are what actually work.

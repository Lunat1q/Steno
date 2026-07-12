# ADR 0013 — Per-word confidence: luminance, not hue

**Status:** accepted · **Date:** 2026-07-12

## Context

whisper.cpp ships a `--print-colors` mode that paints every decoded **token** by its
probability, on a red→green ramp. It is genuinely useful: an average confidence per line tells
you nothing you can act on, but *which word* the model was unsure of tells you exactly which
word not to trust.

whisper.cpp exposes this per token (`whisper_token_data.p`), and Whisper.net surfaces it as
`SegmentData.Tokens[].Probability`. Verified on `jfk.wav`: probabilities range from 0.48 to
0.997 across a single sentence, so there is real signal here, not a flat line.

## The conflict

**Hue is already taken.** In this app colour means *speaker* — You is sky, Them is amber — and
that mapping is load-bearing (ADR 0002, ADR 0008): it is how the user knows who said what
without reading a name tag. Dropping whisper.cpp's red→green ramp on top would put green and
red words inside sky and amber bubbles and destroy the one visual rule the product depends on.

## Decision

**Confidence is carried by luminance; hue stays reserved for speaker identity.**

| Probability | Rendering |
|---|---|
| ≥ 0.85 | full-strength text — where most words land |
| ≥ 0.70 | slightly dimmed |
| ≥ 0.55 | dimmer |
| < 0.55 | faded toward the background |
| < 0.40 | faded **and** a dashed red underline |

Sure words are solid; unsure words fade toward the background — which is what a half-heard
word actually feels like. The underline exists because fading alone is easy to miss on exactly
the word you were about to quote.

Implemented as an attached property (`ConfidenceText.Tokens` on a `TextBlock`) that swaps in
one `Run` per token. When the engine reports no tokens, the `TextBlock`'s own `Text` renders
unchanged, so the line never disappears.

whisper's special tokens (`<|ru|>`, `[_BEG_]`, timestamps) carry probabilities but are not
words, and are dropped — colouring them would be colouring the model's internal punctuation.

A legend under the transcript says what the fading means. Shading the user cannot interpret is
decoration, not information.

## Consequences

- No new inference cost: these probabilities come back from the same decode pass. The only
  addition is `WithProbabilities()`, which was already enabled.
- Confidence shading applies to drafts too, where it is *more* useful — a draft is exactly the
  text you should be unsure about.
- The exporters still write plain text. Confidence is a reading aid on screen; a Markdown file
  full of faded words would be unreadable, and the JSON export already carries a per-line
  `confidence` value for anything downstream.

# ADR 0025 — Dictation is a separate app, not a mode of Steno

**Status:** accepted · **Date:** 2026-07-16

## Problem

Steno turns a microphone into text, but that text only ever lands in Steno's own transcript. A
common want is Windows Voice Typing: speak, and the words appear in whatever text box has focus —
an email, a chat, a code comment. The transcription pipeline already does the hard part; the only
missing pieces are getting text out to the focused window, and a UI that is *just* dictation.

A first cut wired this into Steno itself as a "dictate mode" checkbox. Two things were wrong with
it. It bolted a second, contradictory job onto a screen built around a two-party call (mic +
loopback, speaker colours, recording, save-the-transcript) — and it did not type at all, because the
`INPUT` struct passed to `SendInput` was sized to its keyboard variant instead of its largest
(`MOUSEINPUT`), so `cbSize` never matched and every call silently sent nothing.

## Decision

**A separate executable, `Steno.Dictate.exe`, that reuses `Steno.Core` whole.** It ships next to
`Steno.exe` and shares the same audio capture, whisper.cpp model files and transcription session —
only the UI and the output path differ.

- **UI is three selectors and a button:** microphone, model (Fast / Balanced / Best) and language.
  No loopback, no recording, no exporters, no updater — the things a monologue into another app does
  not need. It is a small always-on-top window ([MainWindow.axaml](../../src/Steno.Dictate/MainWindow.axaml)),
  so it stays visible over the app you are dictating into.
- **Output is `SendInput` with Unicode key events**
  ([TextInjector](../../src/Steno.Core/Platform/Windows/TextInjector.cs), in Core so both apps can
  use it). The OS routes synthetic input to the focused control exactly like a real keyboard, so it
  works in every app with no per-app integration and no accessibility API — the same "no
  integration" property loopback capture gives Steno on the input side. Unicode events carry the
  character directly, so layout, accents, emoji and CJK pass through unchanged.
- **Mic only.** The session runs with the loopback channel off: a monologue into another app has no
  remote party to attribute.
- **Words appear as they are spoken, and are corrected in place.** Waiting for the end of a sentence
  makes dictation feel like sending a letter. So live drafts ([ADR 0010](0010-sub-second-latency.md))
  are *on*, and every draft drives real keystrokes through
  [IncrementalTypist](../../src/Steno.Core/Dictation/IncrementalTypist.cs): each draft
  re-transcribes the same growing audio and shares the utterance's id, so successive drafts agree
  on their leading text. Keep the common prefix, backspace what diverged, type the rest —
  `"recognize"` → `"recognise this"` costs two backspaces, not a retype. The final draft commits
  with a trailing space and the next utterance starts fresh. This is what Windows Voice Typing
  appears to do, and it is the only way to fix a word the model changes its mind about.
- **Never types into itself, and never backspaces into the wrong window.** `TextInjector` no-ops
  when the calling process owns the foreground window. And the view model tracks the window it has
  been typing into: if focus moves mid-utterance, the typist is reset rather than allowed to
  backspace over text nobody dictated.

## One folder, one set of libraries

Both apps publish into the same folder and the MSI ships both, with a Start Menu shortcut each.

They are two front ends over one engine — .NET, Avalonia, `Steno.Core`, Whisper.net — so they were
published **plainly rather than as single-file exes**. Single-file cannot share: each exe embeds its
own copy of every managed assembly, and a second window with three dropdowns in it would have cost
another ~100 MB on disk and ~38 MB of download. Published plainly, `Steno.App.exe` and
`Steno.Dictate.exe` are 200 KB each over one shared set of DLLs and one `runtimes/` folder:

| | MSI |
|---|---|
| Steno alone, single-file | 53 MB |
| Steno + Dictate, shared | **55 MB** |

The price is a wall of DLLs in the install folder, which the old publish profile went out of its way
to avoid. It is the right trade: nobody browses `%LOCALAPPDATA%\Steno`, everybody downloads the MSI.
It also retires the single-file/native-extraction hazard that [ADR 0017](0017-packaging.md) had to
work around — the natives now land in `runtimes/` by ordinary publish rules.

Dictate keeps its choices in `%LOCALAPPDATA%/Steno/dictate.json` — its own file, next to Steno's
`settings.json` ([ADR 0016](0016-remembering-choices.md)). The two apps share a model cache but not
a set of choices, and one writing the other's file would silently drop whatever it did not know
about.

## Consequences

- Steno stays a call-transcription app; dictation evolves on its own without fighting the call UI.
- The `INPUT` union is now sized to `MOUSEINPUT`, and `cbSize` matches — the actual reason nothing
  typed before. `SendInput` cannot be unit-tested without a live desktop and focus, so the covered
  logic is the typist's diff; the injection itself is verified by running the app.
- **Corrections can only fix what dictation typed.** If the user types into the same box while a
  sentence is in flight, a correction backspaces over their characters, because backspace has no
  idea whose text it is eating. Focus changes are handled; a stolen cursor within the same window
  is not.
- Drafts double the inference cost (ADR 0010) and are dropped when whisper falls behind, so on a
  CPU-only machine dictation degrades toward sentence-at-a-time — which is what it did before.
- No global hotkey and no push-to-talk yet: you press Start, then click into the target app.
  Utterance boundaries come from the same VAD as a call ([ADR 0022](0022-vad-over-continuous-audio.md)),
  so a pause is what commits a sentence.

## Alternatives rejected

**A mode inside Steno.** Rejected above: it overloads a screen built for a two-party call with a
one-party job, and the two share almost no UI.

**Clipboard paste (set clipboard, send Ctrl+V).** Faster for long text, but it destroys the
clipboard and some apps swallow or reformat pastes. Keystrokes are what the user would have typed —
the least surprising thing to inject.

**UI Automation `ValuePattern`.** Sets a control's text directly, but terminals, canvas editors and
games expose no such pattern — exactly the "any app" case dictation is for. Worth adding only as a
fallback if a real app rejects synthetic keystrokes.

# ADR 0020 — Record as one stereo WAV; transcribe recordings with the same rules

**Status:** accepted · **Date:** 2026-07-12

## The format decides everything

A recording is only worth keeping if it can be re-transcribed **with the speakers still apart**.
That single requirement rules out the obvious options:

| option | why not |
|---|---|
| One mixed mono file | Throws away the speaker separation that is the entire premise of this app (ADR 0002). Re-transcribing it would need diarization — the thing we exist to avoid. |
| Two mono files | They must be kept together, named consistently, and re-aligned on load. Two files that can be separated are two files that will be separated. |
| **One stereo WAV: left = you, right = them** | **Chosen.** One file, plays in anything, and the channel *is* the speaker — exactly the same structure as a live call, so the offline path is the live path with a different audio source. |

16 kHz, 16-bit PCM (~115 MB/hour). Float32 would double that for accuracy no one can hear and
whisper does not use.

## Recording is written on the session clock, not in arrival order

The two capture devices deliver independently and a loopback device can go quiet for hundreds of
milliseconds. Frames are therefore placed in the file **by their timeline offset**, with any hole
filled by silence. Appending in arrival order would slide the other speaker's voice earlier in the
file with every gap, and a recording whose channels drift apart is worse than no recording at all —
it attributes real words to the wrong person, confidently.

`StereoCallRecorderTests` pins this: a channel that starts late must land in the second half of
the file, not at the beginning.

## Pause does not record

Audio captured while paused is neither transcribed **nor written to disk**. Pause is a privacy
control (ADR 0011); a pause that quietly kept recording would be a betrayal of exactly the thing
the button promises. The recording's timeline consequently skips the paused stretch — correct, and
the honest trade.

## Offline transcription reuses the live rules

`RecordingTranscriber` reads the two channels back out and runs each through **the same segmenter
and the same `TranscriptionPolicy`** as the live pipeline — the energy gate and the hallucination
filter included. That reuse is deliberate: a rule only half the app obeys is a rule that will one
day let "Продолжение следует..." in through the door nobody was watching (ADR 0019).

Differences from live, both deliberate:

- **No drafts.** Nobody is watching a sentence form; drafts would double the work for text that is
  instantly overwritten.
- **No pacing.** It runs as fast as the GPU allows, with a progress bar.

A **mono** file (anything not recorded by Steno) has no speaker separation to recover, so it is
transcribed as a single unnamed "Speaker" rather than pretending to know who is who.

## Consequences

- Recordings are call audio: the most sensitive thing this app touches. They are written in the
  clear to `Documents\Steno\recordings`, and the checkbox says so before the call, not after.
- A recording made with a fast model can be re-transcribed later with a better one. That is the
  main reason to keep the audio at all.

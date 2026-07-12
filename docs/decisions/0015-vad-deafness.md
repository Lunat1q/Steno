# ADR 0015 — The VAD talked itself into deafness

**Status:** accepted · **Date:** 2026-07-12

## Symptom

The app transcribed normally for 15–30 seconds and then stopped producing text for the rest of
the call. The level meters kept moving, so audio was clearly still arriving — it just never
became words again. Restarting fixed it, for another 15–30 seconds.

## Cause

A positive feedback loop in `EnergyVoiceActivityDetector`.

The detector called a frame speech if it was **loud** (energy above an adaptive noise floor)
**and** sounded like a voice (zero-crossing rate in the vocal band). It then adapted its noise
floor on every frame it had judged *not* speech:

```csharp
if (!speech)                                   // <- includes LOUD frames that failed the ZCR test
{
    var rate = rms > _noiseFloor ? FloorAttack : FloorRelease;
    _noiseFloor += (rms - _noiseFloor) * rate; // <- pulled UP toward speech-level energy
}
```

Real speech is not a pure tone. **Fricatives — "s", "sh", "f" — are as loud as vowels but
noise-like**, so their zero-crossing rate lands outside the vocal band and the ZCR test rejects
them. Every such frame was therefore treated as "noise", and taught the noise floor that the
room was as loud as a human voice.

The floor rose. A higher floor meant a higher speech threshold (`floor × 4`). A higher threshold
meant more speech frames failed the loudness test too, which fed *more* speech into the floor.
Within seconds the threshold sat above the speaker's own voice and nothing was ever speech
again — permanently, because with no speech detected there was nothing left to pull the floor
back down.

Reproduced in `VadStabilityTests`: the floor climbs from 9×10⁻⁵ to **2.1×10⁻¹**, a 2,000×
rise, and after the first utterance the detector never fires again.

## Decision

**The noise floor may only learn from frames that are quiet relative to itself.**

```csharp
var loud = rms > AbsoluteFloor && rms > _noiseFloor * SnrFactor;
var speech = loud && IsVocalZeroCrossingRate(frame);

if (!loud)   // energy only — the voice test must never feed back into the floor
    Adapt(rms);
```

The floor now models *the room*, which is what a noise floor is for. Whether a loud frame was a
vowel, a fricative or a cough is irrelevant to it.

Three further guards, because "the microphone stops working" is the worst failure this app has:

- **A hard ceiling** (`MaxNoiseFloor = 0.02`). Speech RMS is 0.1–0.3, so even a fully saturated
  floor keeps the threshold at 0.08 — below any normal voice. Deafness is now unreachable even
  if every heuristic above it is wrong.
- **Rise is slow (0.003), fall is fast (0.05).** Asymmetric on purpose: an over-sensitive
  detector recovers on its own, a deaf one does not.
- **A test that talks for a full minute.** The old tests used 1–2 second pure tones and passed
  happily — the bug needed both *voice-like* audio (with sibilance) and *duration* to appear.
  Anything testing a VAD must have both.

## Lesson

An adaptive threshold that learns from its own classifications will eventually believe itself.
Feedback from a heuristic into the very quantity that heuristic depends on is a loop, and loops
need a hard bound, not just careful coefficients.

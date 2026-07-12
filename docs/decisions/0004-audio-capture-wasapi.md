# ADR 0004 — Audio capture: WASAPI (NAudio), mic + render loopback

**Status:** accepted · **Date:** 2026-07-12

## Context

Two streams are needed: the local microphone, and *whatever the machine is playing*
(the remote party, regardless of whether the call runs in Teams, Zoom, a browser, or
a softphone). Hooking a specific app is a non-starter.

## Decision

**WASAPI**, via **NAudio 2.3**:

- `WasapiCapture` on the selected capture device → local speaker.
- `WasapiLoopbackCapture` on the selected render device → remote speaker(s).
  Loopback captures the render device's mix. It requires no driver, no virtual cable,
  and no per-app integration — any call app works.

Alternatives: virtual audio cable (user must install and reroute — hostile);
per-app process loopback (`ActivateAudioInterfaceAsync` with
`PROCESS_LOOPBACK`, Win 10 2004+ — more precise, more fragile, deferred).

## Normalisation

Device formats vary (48 kHz, stereo, int16 or float32). whisper.cpp accepts exactly
**16 kHz mono float32**. Each capture source therefore: converts to float →
downmixes to mono → resamples to 16 kHz (NAudio `WdlResamplingSampleProvider`) →
emits `AudioFrame`. No other format ever crosses into `Steno.Core`.

## Consequences

- **Windows-only.** `IAudioCaptureSource` is the seam; a PipeWire/CoreAudio
  implementation can be added without touching Core, App, or the pipeline.
- Loopback yields **silence, not frames, when nothing plays** on some devices, and
  stops entirely if the render device is idle. NAudio raises a silent-frame stream;
  the VAD simply sees no speech, which is correct behaviour.
- If the render device is switched mid-call (headset unplugged), the capture dies.
  Handled as a stream error surfaced to the UI; auto-rebind is deferred.

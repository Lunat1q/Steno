# ADR 0009 — WASAPI lives in the MTA; the model download is ours

**Status:** accepted · **Date:** 2026-07-12

Two bugs found on the first real run. Both are recorded because both are the kind that
get silently reintroduced.

## 1. IMMDevice cannot cross COM apartments

**Symptom**

```
Unable to cast COM object of type 'System.__ComObject' to interface type
'NAudio.CoreAudioApi.Interfaces.IMMDevice' ... QueryInterface ... failed
... No such interface supported (0x80004002 (E_NOINTERFACE)).
```

**Cause.** `MMDevice` was created on Avalonia's UI thread (an **STA**), then used after the
first `await ... ConfigureAwait(false)` from a **thread-pool** thread (the **MTA**).
`IMMDevice` has no registered proxy/stub, so COM cannot marshal it between apartments —
the cross-apartment `QueryInterface` fails with `E_NOINTERFACE`. It is not a NAudio bug and
no amount of locking fixes it: the object simply may not leave the apartment that made it.

**Decision.** No WASAPI COM object may cross an apartment boundary.

- `WasapiDeviceProvider.Create` takes and stores the endpoint **id**, not an `MMDevice`.
- `WasapiCaptureSource` resolves the `MMDevice`, constructs the capture, starts, stops and
  disposes it **inside `Task.Run`** — thread-pool threads are all MTA, and the MTA is a single
  process-wide apartment, so device, capture object and NAudio's audio callbacks all sit in it.
- Device *enumeration* still runs on the UI thread, which is fine: those `MMDevice`s are
  created, read and disposed inside that one STA and never handed out.

`WasapiApartmentTests` drives capture from a real STA thread, which is the only way this
regression shows up — a test on the pool would pass with the bug present.

## 2. Silence is not "no callbacks"

A render endpoint playing nothing raises **no** WASAPI callbacks. The original padding logic
lived inside `OnDataAvailable`, so an idle loopback emitted *nothing at all*: the remote
channel's sample clock froze behind the microphone's, and every later line would have been
timestamped too early — the two-timeline guarantee of ADR 0002, gone.

**Decision.** A 100 ms timer synthesises silence whenever the emitted sample count falls more
than 200 ms behind the wall clock, whether or not the device has spoken. The VAD then sees the
pause as the pause it really was.

## 3. Download the model ourselves

Whisper.net's `WhisperGgmlDownloader` returns a non-seekable stream with no length, so a
progress bar over it can only spin. A 1.5 GB download with no visible progress is
indistinguishable from a hang — and that is the app's first-run experience.

**Decision.** `WhisperModelProvider` fetches the GGML file from the ggml-org model repository
with `HttpClient` + `HttpCompletionOption.ResponseHeadersRead`, which yields `Content-Length`
and therefore a real percentage. It downloads to `*.part` and renames on success (a truncated
`.bin` that looks cached would fail to load forever), and has no request timeout, because the
default 100 s would kill a multi-GB download on a slow link.

The model files and their names are unchanged — still exactly the whisper.cpp GGML set.

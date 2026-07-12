# ADR 0021 — Loading the model must not happen on the UI thread

**Status:** accepted · **Date:** 2026-07-12

## Symptom

Pressing **Start** froze the window for a second or two before the "Warming up…" card appeared.

## Cause

`async` is not the same as "off this thread". `WhisperTranscriberFactory.CreateAsync` looked
asynchronous all the way down, but every await *before* the model load completes **synchronously**
once the model is cached on disk:

- `_loadGate.WaitAsync(...)` — the semaphore is free, so it returns a completed task
- `_modelProvider.GetModelPathAsync(...)` — the file exists, so it returns a completed task

An await on an already-completed task does not yield. So control was still on the caller's thread —
the UI thread — when it reached:

```csharp
_whisperFactory = WhisperFactory.FromPath(path);   // synchronous native call, reads 1.5–3 GB
```

Measured: `CreateAsync` held the calling thread for **1,234 ms** before its first real yield, on
`large-v3-turbo`. On **Best** (3 GB) it would be worse. That number *is* the frozen window.

## Decision

The blocking native work — loading the model, and allocating the whisper contexts — runs inside
`Task.Run`:

```
CreateAsync blocks the caller for: 1,234 ms  →  4 ms
```

Total start time is unchanged (~1.6 s): the model still has to load. But it loads on the thread
pool, so the UI stays alive and shows what it is doing instead of going white.

## The rule this is really about

**A method being `async` says nothing about which thread its body runs on.** It yields only when it
awaits something that has not already finished — and a cache hit turns every await above it into a
straight line. If a call does blocking work (native, file, CPU-bound) and might be reached from the
UI thread, it needs an explicit `Task.Run`; `async` alone will not save it.

The way to check is not to read the code but to time it: start the task, and measure how long
before it hands the thread back.

# ADR 0007 — Two projects, not five

**Status:** accepted · **Date:** 2026-07-12

## Decision

```
src/Steno.Core/   class library — audio contracts, segmentation, session, export,
                  and the platform/whisper implementations (in subfolders)
src/Steno.App/    Avalonia desktop — views, viewmodels, composition root
tests/Steno.Core.Tests/
```

The layer diagram in [architecture.md](../architecture.md) shows four boxes
(`Core`, `Platform.Windows`, `Whisper`, `App`). Three of them live as **folders**
inside `Steno.Core`, not as separate assemblies.

## Why not separate assemblies per layer

The compiler-enforced "Core cannot reference NAudio" boundary is the only thing extra
projects buy, and it costs a solution full of csproj plumbing before a single line of
audio works. The boundary is instead enforced by convention and by the fact that
*every* Core type outside `Platform/` and `Whisper/` talks to interfaces
(`IAudioCaptureSource`, `ITranscriber`) which live in `Abstractions/`.

If someone actually ports to Linux, splitting `Platform/` out into its own project is
a folder move and a csproj — the code does not change, because the dependency
direction is already correct.

## Rules that keep this honest

- Nothing under `Session/`, `Segmentation/`, or `Export/` may `using NAudio` or
  `using Whisper.net`. Those two usings are legal **only** in `Platform/` and
  `Whisper/`.
- `Steno.App` constructs the concrete types in exactly one place
  (`Composition/ServiceRegistration.cs`) and everything else is injected.
- Max 300 lines per file, per the project brief.

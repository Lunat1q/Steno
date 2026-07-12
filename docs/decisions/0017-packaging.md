# ADR 0017 — An installer, because a true single exe is not possible

**Status:** accepted · **Date:** 2026-07-12

## Why not one exe

whisper.cpp's native libraries cannot go inside the executable. Whisper.net locates them by
**directory** — it probes `<appDir>/runtimes/<backend>/win-x64/` — while .NET's single-file
publishing extracts native libraries **flat** into a temp folder. That folder has no `runtimes`
tree, and the CPU and Vulkan builds would collide inside it anyway: both are named `whisper.dll`.

This was verified, not assumed. `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract`
produces an exe that starts perfectly, shows the UI, and then throws

```
FileNotFoundException: Native Library not found in default paths.
```

**the moment the user presses Start** — the worst possible failure shape, because every smoke
test that only checks "does the window open" passes.

The workaround — embed the DLLs as resources and unpack them to `%LOCALAPPDATA%` on first run —
was built and then thrown away. It is a single file in name only: it still writes a folder of
native libraries to disk, just later, less visibly, and with new failure modes (a half-unpacked
`whisper.dll` fails in ways far nastier than a missing one). If the app is going to put a folder
on disk, an installer should do it, honestly and reversibly.

## What ships

```
installer/build.ps1   ->  artifacts/Steno-Setup.msi   (53 MB, 192 MB installed)
```

- **Every managed assembly is still bundled into `Steno.App.exe`** (`PublishSingleFile`), so
  there is no wall of DLLs — just the exe, three Avalonia natives, and `runtimes/`.
- **Self-contained**: no .NET install needed on the target machine.
- **WiX 5 as a `dotnet tool`**, so building the installer needs no system-wide toolchain and no
  admin rights.
- **Per-user install** (`Scope="perUser"`, into `%LOCALAPPDATA%`): Steno needs no privileges, its
  models and settings already live under `%LOCALAPPDATA%`, and a UAC prompt to install a
  transcription app is a cost with no benefit. Upgrades don't elevate either.
- Publisher: **TiQ Studio**. Start-menu shortcut, proper Add/Remove Programs entry with the icon.

## The one rule for anyone touching packaging

**The `runtimes/` folder layout must survive into the install directory.** Flattening it, or
"tidying" the DLLs next to the exe, breaks transcription — and breaks it *late*, at the moment
the user presses Start, not at startup. `installer/build.ps1` fails the build if
`runtimes/vulkan/win-x64/whisper.dll` is missing from the publish output, so this cannot ship
broken silently.

## Sizes (measured)

| | |
|---|---|
| `Steno.App.exe` (all managed code + Avalonia) | 115 MB |
| `ggml-vulkan-whisper.dll` (the GPU backend) | 55 MB |
| everything else | ~22 MB |
| **MSI (compressed)** | **53 MB** |

ReadyToRun was tested and rejected: +48 MB to save a few hundred ms of JIT on a startup already
dominated by loading a multi-GB model.

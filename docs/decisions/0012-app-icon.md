# ADR 0012 — App icons: the generated .ico is the committed artifact

**Status:** accepted · **Date:** 2026-07-12 · **Amended:** 2026-07-16 (a second app, a second icon)

## Context

Each app has an identity: a typewriter for Steno, a microphone for Steno Dictate
([ADR 0025](0025-dictate-mode.md)). Windows wants an `.ico` for the executable's Win32 resource,
and Avalonia wants a loadable resource for the window/taskbar icon at runtime. Neither consumes SVG.

## Decision

- **The rendered `Assets/icon.ico` (16/24/32/48/64/128/256 px, PNG-compressed entries) and
  `icon.png` (256 px) are committed** under each app's project, not generated at build time.
- `<ApplicationIcon>` bakes the `.ico` into the exe; `<AvaloniaResource Include="Assets\**" />`
  ships it for `Window.Icon="/Assets/icon.ico"`. The MSI gives each Start Menu shortcut its own
  `<Icon>`, so the two entries are told apart at a glance.
- **The SVG originals are not kept in the repo.** They are rasterised once and discarded; the
  `.ico` is the artifact that matters. Dictate's came from
  [SVG Repo](https://www.svgrepo.com) ("voice recording radio", 512×512); Steno's typewriter
  predates this note. Re-cutting an icon means fetching or redrawing the source — accepted, for a
  file that changes about once a year.

## Why commit the generated files

Rasterising SVG needs a renderer, and there is none in the .NET SDK: doing it at build time
would drag a Skia/ImageMagick dependency (or an external tool) into every build, on every
machine, forever — to produce a file that changes roughly never. Each icon was rendered once
with Svg.Skia in a throwaway project: load the SVG, fit its `CullRect` into each square, encode
every entry as PNG, and write the ICO container by hand (6-byte `ICONDIR`, one 16-byte
`ICONDIRENTRY` each, then the payloads — a `0` width byte means 256).

**Re-cutting an icon is a manual job**, and deliberately so: fetch or redraw the SVG, re-render,
commit the `.ico`. The build does not check anything is in sync, because there is no source in the
tree to be out of sync with.

At 16 px the Dictate microphone reads as a yellow blob — the grille dots and the thin stand ring
collapse. That is inherent to rasterising a detailed drawing that small, and it only shows in
Explorer's small-icon view; 32 px and up are clean. A hand-simplified 16 px entry is the fix if it
ever matters.

## Note

Every `.ico` entry is PNG-compressed, including the small sizes. Windows 10/11, Explorer and
Avalonia all accept this; only pre-Vista shells require BMP entries, and they are not a target.

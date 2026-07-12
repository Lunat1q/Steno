# ADR 0016 — Remember what the user picked

**Status:** accepted · **Date:** 2026-07-12

## Context

Quality reset to **Balanced** on every launch, so anyone who prefers **Best** re-picked it
before every call. The same applies to the microphone, the speakers, the language and the
toggles: the app asked the same questions every time and ignored the same answers.

## Decision

`%LOCALAPPDATA%/Steno/settings.json` holds the last setup: quality, language, device ids and
the three toggles. Written the moment a choice is made — there is no Apply button to forget —
and restored on launch.

Devices are remembered **by endpoint id**, not by index or name: a USB headset that comes back
on a different port keeps its id, whereas an index would silently select someone else's
microphone.

## Failure behaviour, which is the whole design

A preferences file is a thing that will one day be corrupt, unreadable, or refer to hardware
that no longer exists. None of those may cost the user the app:

- **Corrupt or unreadable JSON** → log, fall back to defaults. Losing a preference costs one
  re-pick; throwing on startup costs the app.
- **A device that is gone** (unplugged headset) → fall back to the system default, so the
  screen is never left in a state where Start cannot be pressed.
- **Disk full on save** → swallowed. Failing to store a preference must not crash a live call.

## A bug worth recording

The first implementation loaded the settings *after* enumerating devices. Enumeration raises
property changes, save-on-change wrote those defaults to disk, and the load then read back the
file it had just overwritten — so nothing was ever remembered. Construction now writes nothing
at all, and `Starting_fresh_does_not_clobber_saved_settings_before_reading_them` pins it.

Save-on-change and load-on-start are a read/write ordering problem, not a storage problem.

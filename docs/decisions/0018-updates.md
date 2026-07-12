# ADR 0018 — Updates from GitHub Releases, verified before they run

**Status:** accepted · **Date:** 2026-07-12

## The feed: GitHub Releases, not a GitHub Pages file

A gh-pages `latest.json` was considered and rejected. GitHub Releases already publishes exactly
this information (`/repos/Lunat1q/Steno/releases/latest`: tag, notes, assets, sizes) and it is
where the installer lives anyway. A second feed would be a second source of truth, and the day
the page and the release disagree is the day the updater installs the wrong thing — or offers a
version that does not exist.

No page is needed. (A landing page is a fine thing to want; it just should not be load-bearing
for updates.)

## The payload is executable, so it is treated as such

`MsiUpdateInstaller` downloads a file and then runs it. That is the most dangerous thing this
app does, so the checks are deliberately unforgiving:

- **HTTPS only, and an exact host match** against a small allow-list (`github.com`,
  `objects.githubusercontent.com`, …). Not a suffix match — `github.com.evil.example.com` is
  refused, and there is a test that says so.
- **The SHA-256 must be published with the release and must match.** A release without a
  checksum is **refused, not trusted**: `installer/release.ps1` always publishes one, so its
  absence means something is wrong rather than something is fine.
- Verification happens **before** the MSI is handed to Windows Installer. A mismatched file is
  deleted, not kept "just in case".

Note honestly what this does *not* buy: the checksum comes from the same GitHub release as the
installer, so it proves the download was not corrupted or swapped in transit — it does not
protect against a compromised GitHub account. Only code signing would, and that needs a
certificate.

## Behaviour

Checked silently in the background at launch. Failure is silent too: being offline is not news,
and a modal telling the user their update check failed is pure noise.

**The offer is hidden while a call is running, or while an unsaved transcript is on screen.**
Installing quits the app (an MSI cannot replace a running exe), so interrupting a live
transcription to install a patch would be the rudest thing this app could do. The banner waits.

The user must agree. Then: download with a progress bar, verify, launch `msiexec /passive`, and
exit so the files can be replaced. The MSI is a `MajorUpgrade`, so it upgrades in place rather
than installing a second copy.

## Releasing

```
installer/release.ps1 -Version 1.1.0
```

Builds the MSI, writes `Steno-Setup.msi.sha256`, and publishes both as a GitHub release.
**A release published by hand, without the checksum file, will be ignored by every installed
copy of Steno** — by design.

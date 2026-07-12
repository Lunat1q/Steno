<#
.SYNOPSIS
    Builds the installer and publishes it as a GitHub release — the feed the in-app updater reads.

.DESCRIPTION
    The SHA-256 file is not optional. MsiUpdateInstaller REFUSES to install a release that does
    not publish one, so a release made by hand, without this script, will be ignored by every
    installed copy of Steno.

.EXAMPLE
    installer/release.ps1 -Version 1.1.0
    installer/release.ps1 -Version 1.1.0 -Draft
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Notes = "",
    [switch] $Draft
)

$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repo "artifacts"
$msi = Join-Path $artifacts "Steno-Setup.msi"
$sha = "$msi.sha256"

& (Join-Path $PSScriptRoot "build.ps1") -Version $Version
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

# The updater verifies this before running anything it downloaded.
$hash = (Get-FileHash $msi -Algorithm SHA256).Hash
Set-Content -Path $sha -Value "$hash  Steno-Setup.msi" -NoNewline
Write-Host "SHA-256: $hash" -ForegroundColor Cyan

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "The GitHub CLI (gh) is required to publish a release."
}

$tag = "v$Version"

# Not $args: that is a PowerShell automatic variable, and writing to it is a trap.
$ghArgs = @("release", "create", $tag, $msi, $sha,
            "--title", "Steno $Version",
            "--notes", ($Notes ? $Notes : "Steno $Version"))

if ($Draft) { $ghArgs += "--draft" }

Write-Host "Publishing $tag …" -ForegroundColor Cyan
gh @ghArgs
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }

Write-Host "Released $tag. Installed copies will offer it on next launch." -ForegroundColor Green

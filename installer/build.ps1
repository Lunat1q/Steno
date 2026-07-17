<#
.SYNOPSIS
    Publishes Steno and Steno Dictate into one folder and builds the installer (Steno-Setup.msi).

.EXAMPLE
    installer/build.ps1
    installer/build.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [string] $Version = "1.0.0",
    [switch] $SkipPublish
)

$ErrorActionPreference = "Stop"

$repo = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $repo "bin\publish"
$outputDir = Join-Path $repo "artifacts"
$msi = Join-Path $outputDir "Steno-Setup.msi"

<#
  Both apps publish into the SAME folder, and deliberately NOT as single-file exes.

  They are two front ends over one engine — .NET itself, Avalonia, Steno.Core, Whisper.net. Bundled
  into single-file exes those libraries cannot be shared: each exe carries its own copy and the
  install doubles for a second window with three dropdowns in it. Published plainly, the two small
  exes sit next to one set of DLLs and one runtimes/ folder. ADR 0025.

  A wall of DLLs in the install folder is the price, and it buys back roughly half the download.
  Nobody browses %LOCALAPPDATA%\Steno; everybody downloads the MSI.
#>
$publishArgs = @(
    "-c", "Release"
    "-r", "win-x64"
    "--self-contained", "true"
    "-p:PublishSingleFile=false"
    "-p:PublishTrimmed=false"      # Avalonia resolves controls and bindings reflectively
    "-p:PublishReadyToRun=false"   # measured: +48 MB to save a few hundred ms of JIT. Bad trade.
    "-p:DebugType=embedded"
    "-p:DebugSymbols=false"
    "-p:SatelliteResourceLanguages=en"
    "-p:AllowedReferenceRelatedFileExtensions=none"
    "-p:Version=$Version"
    "--nologo"
    "-v", "quiet"
)

if (-not $SkipPublish) {
    Write-Host "Publishing Steno and Steno Dictate ($Version)…" -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    foreach ($project in @("src\Steno.App\Steno.App.csproj", "src\Steno.Dictate\Steno.Dictate.csproj")) {
        dotnet publish (Join-Path $repo $project) @publishArgs -o $publishDir
        if ($LASTEXITCODE -ne 0) { throw "publish failed: $project" }
    }
}

foreach ($exe in @("Steno.App.exe", "Steno.Dictate.exe")) {
    if (-not (Test-Path (Join-Path $publishDir $exe))) {
        throw "No published $exe at $publishDir. Run without -SkipPublish."
    }
}

# The natives are the whole reason this is an installer and not one exe: Whisper.net finds
# whisper.cpp by directory, so this layout must survive into the install folder (ADR 0017).
if (-not (Test-Path (Join-Path $publishDir "runtimes\vulkan\win-x64\whisper.dll"))) {
    throw "whisper.cpp natives are missing from the publish output — the installed app would fail on Start."
}

# Leftovers the publish drags in whatever the symbol properties say. Done here rather than in a
# per-project target so it cleans the shared folder once, whichever app published into it last.
#   .pdb  — Skia/HarfBuzz ship ~100 MB of native symbols as package content
#   .so / .dylib and other architectures — whisper.cpp's packages copy every platform's binaries
Get-ChildItem $publishDir -Recurse -Include *.pdb, *.so, *.dylib -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
foreach ($rid in @("win-x86", "win-arm64")) {
    $dir = Join-Path $publishDir "runtimes\$rid"
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}

# WiX ships as a dotnet tool, so the installer needs no system-wide toolchain.
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    Write-Host "Installing the WiX dotnet tool…" -ForegroundColor Cyan
    dotnet tool install --global wix --version "5.*" | Out-Null
    $env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:PATH"
}

wix extension add -g WixToolset.UI.wixext/5.0.2 | Out-Null

New-Item -ItemType Directory -Force $outputDir | Out-Null

Write-Host "Building $msi …" -ForegroundColor Cyan
$wxs = Join-Path $PSScriptRoot "Steno.wxs"
$icon = Join-Path $repo "src\Steno.App\Assets\icon.ico"
$dictateIcon = Join-Path $repo "src\Steno.Dictate\Assets\icon.ico"

wix build $wxs `
    -ext WixToolset.UI.wixext `
    -arch x64 `
    -d Version=$Version `
    -d PublishDir=$publishDir `
    -d InstallerDir=$PSScriptRoot `
    -d IconFile=$icon `
    -d DictateIconFile=$dictateIcon `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

$size = (Get-Item $msi).Length / 1MB
Write-Host ("Done: {0} ({1:N0} MB)" -f $msi, $size) -ForegroundColor Green

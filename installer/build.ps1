<#
.SYNOPSIS
    Publishes Steno and builds the Windows installer (Steno-Setup.msi).

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
$publishDir = Join-Path $repo "src\Steno.App\bin\publish"
$outputDir = Join-Path $repo "artifacts"
$msi = Join-Path $outputDir "Steno-Setup.msi"

if (-not $SkipPublish) {
    Write-Host "Publishing Steno ($Version)…" -ForegroundColor Cyan
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    dotnet publish (Join-Path $repo "src\Steno.App\Steno.App.csproj") `
        -p:PublishProfile=SingleFile `
        -p:Version=$Version `
        --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
}

if (-not (Test-Path (Join-Path $publishDir "Steno.App.exe"))) {
    throw "No published app at $publishDir. Run without -SkipPublish."
}

# The natives are the whole reason this is an installer and not one exe: Whisper.net finds
# whisper.cpp by directory, so this layout must survive into the install folder (ADR 0017).
if (-not (Test-Path (Join-Path $publishDir "runtimes\vulkan\win-x64\whisper.dll"))) {
    throw "whisper.cpp natives are missing from the publish output — the installed app would fail on Start."
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

wix build $wxs `
    -ext WixToolset.UI.wixext `
    -arch x64 `
    -d Version=$Version `
    -d PublishDir=$publishDir `
    -d InstallerDir=$PSScriptRoot `
    -d IconFile=$icon `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

$size = (Get-Item $msi).Length / 1MB
Write-Host ("Done: {0} ({1:N0} MB)" -f $msi, $size) -ForegroundColor Green

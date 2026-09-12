<#
.SYNOPSIS
  Baut das Trainer-ASI (x86) und legt es optional ins Spielverzeichnis.

.DESCRIPTION
  GTA IV ist 32-bit, also wird zwingend fuer x86 gebaut. Eine x64-DLL wuerde
  vom ASI-Loader kommentarlos ignoriert - ein Fehler, der sich als "nichts
  passiert" aeussert und lange gesucht wird.

  Die C-Laufzeit wird statisch gelinkt (/MT). Ein Trainer, der eine
  Redistributable voraussetzt, waere ausgerechnet hier fehl am Platz: genau an
  einer fehlenden Visual-C++-Laufzeit ist das Spiel nach dem Downgrade zuerst
  gescheitert.

.PARAMETER Deploy
  Kopiert das fertige ASI nach <Spiel>\plugins\. Braucht Administratorrechte,
  wenn das Spiel unter Program Files liegt.

.EXAMPLE
  .\scripts\build-trainer.ps1
  .\scripts\build-trainer.ps1 -Deploy
#>
[CmdletBinding()]
param(
    [switch] $Deploy,
    [string] $GamePath = "C:\Program Files\Rockstar Games\Grand Theft Auto IV",
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root    = Split-Path -Parent $PSScriptRoot
$source  = Join-Path $root "src\Trainer"
$out     = Join-Path $root "artifacts\trainer"
$obj     = Join-Path $out "obj"
$asiName = "ModlauncherIV-Trainer.asi"

# ---------------------------------------------------------------- Toolchain

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "vswhere nicht gefunden. Visual Studio Build Tools mit C++ (x86) installieren."
}

$vsPath = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if (-not $vsPath) {
    throw "Keine Installation mit C++-Werkzeugen gefunden (Workload 'VCTools')."
}

$vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvarsall.bat"
if (-not (Test-Path $vcvars)) { throw "vcvarsall.bat fehlt unter $vsPath" }

Write-Host "Toolchain: $vsPath" -ForegroundColor Cyan

# ------------------------------------------------------------------- Bauen

New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null

$sources = @(
    (Join-Path $source "dllmain.cpp"),
    (Join-Path $source "core\Log.cpp"),
    (Join-Path $source "game\GameVersion.cpp")
)

$flags = if ($Configuration -eq "Release") { "/O2 /DNDEBUG" } else { "/Od /Zi /D_DEBUG" }

# /MT statt /MD: statische Laufzeit, keine Redistributable noetig.
# /EHsc: ein Wurf im Tick darf das Spiel nicht mitnehmen.
$compile = @(
    "cl.exe",
    "/nologo /std:c++20 /W4 /WX /EHsc /MT /LD $flags",
    "/permissive- /Zc:__cplusplus",
    "/I`"$source`"",
    "/Fo`"$obj\\`"",
    "/Fe`"$out\$asiName`"",
    ($sources | ForEach-Object { "`"$_`"" }) -join " ",
    "/link /MACHINE:X86 /SUBSYSTEM:WINDOWS"
) -join " "

Write-Host "Baue $asiName ($Configuration, x86) ..." -ForegroundColor Cyan

# x86 zwingend - das ist keine Bequemlichkeit, sondern Voraussetzung.
$cmd = "`"$vcvars`" x86 >nul && $compile"
& cmd.exe /c $cmd
if ($LASTEXITCODE -ne 0) { throw "Compiler-Aufruf fehlgeschlagen (Exitcode $LASTEXITCODE)." }

$asi = Join-Path $out $asiName
if (-not (Test-Path $asi)) { throw "Kein $asiName erzeugt." }

$info = Get-Item $asi
Write-Host ("Fertig: {0} ({1:N0} Bytes)" -f $asi, $info.Length) -ForegroundColor Green

# ---------------------------------------------------------------- Signieren

& (Join-Path $PSScriptRoot "sign.ps1") -Path $asi | Out-Null
Write-Host "Signiert." -ForegroundColor Green

# ------------------------------------------------------------------ Deploy

if (-not $Deploy) {
    Write-Host "Mit -Deploy landet es in <Spiel>\plugins\."
    exit 0
}

$plugins = Join-Path $GamePath "plugins"
if (-not (Test-Path $plugins)) {
    throw "Kein plugins-Ordner unter $GamePath. Zuerst: mliv apply ultimate-asi-loader"
}

if (Get-Process -Name GTAIV -ErrorAction SilentlyContinue) {
    throw "GTAIV.exe laeuft - erst beenden, sonst ist die Datei gesperrt."
}

try {
    Copy-Item $asi (Join-Path $plugins $asiName) -Force
    Write-Host "Kopiert nach $plugins" -ForegroundColor Green
}
catch {
    throw "Kopieren fehlgeschlagen: $($_.Exception.Message)`nAls Administrator ausfuehren."
}

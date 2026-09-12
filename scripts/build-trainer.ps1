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

# vswhere ist der saubere Weg, aber es fehlt manchmal - etwa direkt nach einer
# noch nicht abgeschlossenen Installation. Dann suchen wir vcvarsall.bat selbst,
# statt an einem Hilfswerkzeug zu scheitern, waehrend der Compiler laengst da ist.
$vcvars = $null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    if ($vsPath) {
        $candidate = Join-Path $vsPath "VC\Auxiliary\Build\vcvarsall.bat"
        if (Test-Path $candidate) { $vcvars = $candidate }
    }
}

if (-not $vcvars) {
    $vcvars = Get-ChildItem -Path "C:\Program Files\Microsoft Visual Studio",
                                  "C:\Program Files (x86)\Microsoft Visual Studio" `
                            -Filter "vcvarsall.bat" -Recurse -ErrorAction SilentlyContinue |
              Select-Object -First 1 -ExpandProperty FullName
}

if (-not $vcvars) {
    throw "vcvarsall.bat nicht gefunden. Visual Studio Build Tools mit C++ (x86) installieren."
}

Write-Host "Toolchain: $vcvars" -ForegroundColor Cyan

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
# Bewusst als durchgehende Zeichenkette und nicht als Array mit -join:
# in einem Array-Literal frisst "-join" das folgende Komma als Teil seines
# rechten Operanden, macht daraus ein Trennzeichen-Array und baut damit einen
# stillen Unsinn, den erst die erzeugte Batch-Datei sichtbar macht.
$sourceArgs = ($sources | ForEach-Object { '"' + $_ + '"' }) -join ' '

$compile = "cl.exe /nologo /std:c++20 /W4 /WX /EHsc /MT /LD $flags " +
           "/permissive- /Zc:__cplusplus " +
           "/I`"$source`" /Fo`"$obj\\`" /Fe`"$out\$asiName`" " +
           "$sourceArgs " +
           "/link /MACHINE:X86 /SUBSYSTEM:WINDOWS"

Write-Host "Baue $asiName ($Configuration, x86) ..." -ForegroundColor Cyan

# Ueber eine Batch-Datei statt ueber "cmd /c <langer String>": der Aufruf
# enthaelt Pfade mit Leerzeichen, Anfuehrungszeichen und && - beim Durchreichen
# durch PowerShell an cmd zerfaellt das zuverlaessig, und zwar ohne jede
# Fehlermeldung des Compilers. Eine Datei hat dieses Problem nicht.
$batch = Join-Path $out "build.cmd"
@(
    "@echo off",
    "call `"$vcvars`" x86 >nul",
    "if errorlevel 1 exit /b 1",
    $compile,
    "exit /b %ERRORLEVEL%"
) | Set-Content -Path $batch -Encoding ASCII

# Unter Windows PowerShell 5.1 macht "Stop" aus jeder stderr-Zeile eines nativen
# Programms einen abbrechenden NativeCommandError. vcvarsall.bat warnt auf
# stderr ueber ein fehlendes vswhere.exe und arbeitet trotzdem korrekt weiter -
# ohne diese Ausnahme scheitert der Build an einer blossen Warnung.
$previous = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & cmd.exe /c $batch 2>&1 | ForEach-Object { "  $_" }
    $code = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previous
}

if ($code -ne 0) { throw "Compiler-Aufruf fehlgeschlagen (Exitcode $code)." }

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

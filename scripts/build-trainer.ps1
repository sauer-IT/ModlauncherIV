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
    [switch] $Test,
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

# ---------------------------------------------------------------------- SDK

# Das IV-SDK wird geholt und geprueft, nicht mitgeliefert - dieselbe Haltung wie
# beim Launcher. Festgenagelt auf einen Commit statt auf einen Branch: ein
# Branch-Archiv aendert sich unter der Hand, ein Commit-Archiv nie. Damit ist
# die Pruefsumme ueberhaupt erst sinnvoll.
$sdkCommit = "3fb076443afd1b3d5557c9c329bd2131065da99e"
$sdkSha256 = "271b1ae06d1096f3f3936fc1a8aa2c1f104ed40156363412de8a75c236f0fe63"
$sdkRoot   = Join-Path $root "third_party\iv-sdk"
$sdkInclude = Join-Path $sdkRoot "iv-sdk-$sdkCommit\include"

if (-not (Test-Path (Join-Path $sdkInclude "IVSDK.cpp"))) {
    Write-Host "Hole IV-SDK ($($sdkCommit.Substring(0,7))) ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null

    $zip = Join-Path $sdkRoot "iv-sdk.zip"
    Invoke-WebRequest -Uri "https://github.com/PHARTGAMES/iv-sdk/archive/$sdkCommit.zip" `
                      -OutFile $zip -UseBasicParsing

    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $sdkSha256) {
        Remove-Item $zip -Force
        throw "SDK-Pruefsumme stimmt nicht.`n  erwartet $sdkSha256`n  erhalten $actual"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $sdkRoot)
    Remove-Item $zip -Force
    Write-Host "IV-SDK verifiziert und entpackt." -ForegroundColor Green
}

if (-not (Test-Path (Join-Path $sdkInclude "IVSDK.cpp"))) {
    throw "IV-SDK unvollstaendig unter $sdkInclude"
}

# --------------------------------------------------------------------- D3DX
#
# Das SDK bindet d3dx9.h ein. Die Header gehoerten zum DirectX SDK von Juni
# 2010, das Microsoft eingestellt hat und dessen Installer fuer den Fehler
# S1023 beruechtigt ist. Dieselben Dateien liegen als NuGet-Paket vor - das ist
# ein ZIP, kein Installer, und laesst sich wie alles andere hier holen und
# pruefen, ohne am System etwas zu veraendern.
$d3dxVersion = "9.29.952.8"
$d3dxSha256  = "ead0906ae8a26c18a7525da7490127a2110f7c58f18293738283e30e97c6ea4b"
$d3dxRoot    = Join-Path $root "third_party\d3dx"
$d3dxInclude = Join-Path $d3dxRoot "build\native\include"
$d3dxLib     = Join-Path $d3dxRoot "build\native\release\lib\x86"

if (-not (Test-Path (Join-Path $d3dxInclude "d3dx9.h"))) {
    Write-Host "Hole D3DX-Header ($d3dxVersion) ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $d3dxRoot | Out-Null

    $pkg = Join-Path $d3dxRoot "d3dx.zip"
    Invoke-WebRequest -UseBasicParsing -OutFile $pkg `
        -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.dxsdk.d3dx/$d3dxVersion/microsoft.dxsdk.d3dx.$d3dxVersion.nupkg"

    $actual = (Get-FileHash $pkg -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $d3dxSha256) {
        Remove-Item $pkg -Force
        throw "D3DX-Pruefsumme stimmt nicht.`n  erwartet $d3dxSha256`n  erhalten $actual"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($pkg, $d3dxRoot)
    Remove-Item $pkg -Force
    Write-Host "D3DX verifiziert und entpackt." -ForegroundColor Green
}

if (-not (Test-Path (Join-Path $d3dxInclude "d3dx9.h"))) {
    throw "d3dx9.h fehlt unter $d3dxInclude"
}

# ------------------------------------------------------------------- Bauen

New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null

$sources = @(
    (Join-Path $source "plugin.cpp"),
    (Join-Path $source "core\Log.cpp"),
    (Join-Path $source "game\GameVersion.cpp"),
    (Join-Path $source "menu\Menu.cpp")
)

# Der Menue-Pruefstand ist eine Konsolenanwendung, kein ASI. Er haengt an keiner
# Spielfunktion und laeuft deshalb hier, statt erst im Spiel.
if ($Test) {
    $testExe = Join-Path $out "menu-test.exe"
    $testCompile = "cl.exe /nologo /std:c++20 /W4 /WX /EHsc /MT /O2 " +
                   "/Fo`"$obj\test\\`" /Fe`"$testExe`" " +
                   "`"$source\test\menu-test.cpp`" `"$source\menu\Menu.cpp`""

    New-Item -ItemType Directory -Force -Path (Join-Path $obj "test") | Out-Null
    $testBatch = Join-Path $out "build-test.cmd"
    @(
        "@echo off",
        "call `"$vcvars`" x86 >nul",
        "if errorlevel 1 exit /b 1",
        $testCompile,
        "exit /b %ERRORLEVEL%"
    ) | Set-Content -Path $testBatch -Encoding ASCII

    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & cmd.exe /c $testBatch 2>&1 | Where-Object { $_ -notmatch 'vswhere|konnte nicht gefunden' } |
            ForEach-Object { "  $_" }
        $testCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }

    if ($testCode -ne 0) { throw "Pruefstand liess sich nicht bauen (Exitcode $testCode)." }

    & $testExe
    if ($LASTEXITCODE -ne 0) { throw "Menue-Pruefstand fehlgeschlagen." }
    exit 0
}

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
           "/I`"$source`" /I`"$sdkInclude`" /I`"$d3dxInclude`" " +
           "/Fo`"$obj\\`" /Fe`"$out\$asiName`" " +
           "$sourceArgs " +
           "/link /MACHINE:X86 /SUBSYSTEM:WINDOWS /LIBPATH:`"$d3dxLib`" user32.lib"

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

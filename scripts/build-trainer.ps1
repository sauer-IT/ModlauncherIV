<#
.SYNOPSIS
  Builds the trainer ASI (x86) and optionally drops it into the game directory.

.DESCRIPTION
  GTA IV is 32-bit, so the build is x86, no exceptions. An x64 DLL would be
  ignored by the ASI loader without comment - a bug that presents as "nothing
  happens" and gets hunted for a long time.

  The C runtime is linked statically (/MT). A trainer that requires a
  redistributable would be out of place here of all things: a missing Visual C++
  runtime is exactly what the game first failed on after the downgrade.

.PARAMETER Deploy
  Copies the finished ASI into <game>\plugins\. Needs administrator rights when
  the game sits under Program Files.

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
$asiName = "sauer.asi"

# ---------------------------------------------------------------- Toolchain

# vswhere is the clean route, but it is sometimes missing - right after an
# unfinished installation, say. Then we look for vcvarsall.bat ourselves rather
# than failing on a helper tool while the compiler has been there all along.
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
    throw "vcvarsall.bat not found. Install the Visual Studio Build Tools with C++ (x86)."
}

Write-Host "Toolchain: $vcvars" -ForegroundColor Cyan

# ---------------------------------------------------------------------- SDK

# The IV-SDK is fetched and verified, not shipped - the same stance as in the
# launcher. Pinned to a commit rather than a branch: a branch archive changes
# under your hands, a commit archive never does. Only that makes the checksum
# meaningful in the first place.
$sdkCommit = "3fb076443afd1b3d5557c9c329bd2131065da99e"
$sdkSha256 = "271b1ae06d1096f3f3936fc1a8aa2c1f104ed40156363412de8a75c236f0fe63"
$sdkRoot   = Join-Path $root "third_party\iv-sdk"
$sdkInclude = Join-Path $sdkRoot "iv-sdk-$sdkCommit\include"

if (-not (Test-Path (Join-Path $sdkInclude "IVSDK.cpp"))) {
    Write-Host "Fetching the IV-SDK ($($sdkCommit.Substring(0,7))) ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null

    $zip = Join-Path $sdkRoot "iv-sdk.zip"
    Invoke-WebRequest -Uri "https://github.com/PHARTGAMES/iv-sdk/archive/$sdkCommit.zip" `
                      -OutFile $zip -UseBasicParsing

    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $sdkSha256) {
        Remove-Item $zip -Force
        throw "SDK checksum does not match.`n  expected $sdkSha256`n  got      $actual"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $sdkRoot)
    Remove-Item $zip -Force
    Write-Host "IV-SDK verified and extracted." -ForegroundColor Green
}

if (-not (Test-Path (Join-Path $sdkInclude "IVSDK.cpp"))) {
    throw "IV-SDK incomplete under $sdkInclude"
}

# --------------------------------------------------------------------- D3DX
#
# The SDK includes d3dx9.h. Those headers belonged to the June 2010 DirectX SDK,
# which Microsoft discontinued and whose installer is notorious for error S1023.
# The same files exist as a NuGet package - that is a ZIP, not an installer, and
# can be fetched and verified like everything else here, without changing
# anything about the system.
$d3dxVersion = "9.29.952.8"
$d3dxSha256  = "ead0906ae8a26c18a7525da7490127a2110f7c58f18293738283e30e97c6ea4b"
$d3dxRoot    = Join-Path $root "third_party\d3dx"
$d3dxInclude = Join-Path $d3dxRoot "build\native\include"
$d3dxLib     = Join-Path $d3dxRoot "build\native\release\lib\x86"

if (-not (Test-Path (Join-Path $d3dxInclude "d3dx9.h"))) {
    Write-Host "Fetching the D3DX headers ($d3dxVersion) ..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $d3dxRoot | Out-Null

    $pkg = Join-Path $d3dxRoot "d3dx.zip"
    Invoke-WebRequest -UseBasicParsing -OutFile $pkg `
        -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.dxsdk.d3dx/$d3dxVersion/microsoft.dxsdk.d3dx.$d3dxVersion.nupkg"

    $actual = (Get-FileHash $pkg -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $d3dxSha256) {
        Remove-Item $pkg -Force
        throw "D3DX checksum does not match.`n  expected $d3dxSha256`n  got      $actual"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($pkg, $d3dxRoot)
    Remove-Item $pkg -Force
    Write-Host "D3DX verified and extracted." -ForegroundColor Green
}

if (-not (Test-Path (Join-Path $d3dxInclude "d3dx9.h"))) {
    throw "d3dx9.h missing under $d3dxInclude"
}

# ---------------------------------------------------------------- Building

New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null

$sources = @(
    (Join-Path $source "plugin.cpp"),
    (Join-Path $source "core\Log.cpp"),
    (Join-Path $source "core\Config.cpp"),
    (Join-Path $source "game\GameVersion.cpp"),
    (Join-Path $source "menu\Menu.cpp")
)

# The test harnesses are console applications, not ASIs. They depend on no game
# function and therefore run here rather than only in the game.
#
# That is exactly why menu and configuration are free of game and platform
# headers: a navigation bug or an unrecognised key shows up in milliseconds
# instead of after a game start and a loading screen.
if ($Test) {
    $suites = @(
        @{ Name = "Menu";          Exe = "menu-test.exe";   Files = @("test\menu-test.cpp", "menu\Menu.cpp") },
        @{ Name = "Configuration"; Exe = "config-test.exe"; Files = @("test\config-test.cpp", "core\Config.cpp") }
    )

    New-Item -ItemType Directory -Force -Path (Join-Path $obj "test") | Out-Null

    foreach ($suite in $suites) {
        $testExe = Join-Path $out $suite.Exe
        $files = ($suite.Files | ForEach-Object { "`"$source\$_`"" }) -join " "

        $testCompile = "cl.exe /nologo /std:c++20 /W4 /WX /EHsc /MT /O2 " +
                       "/Fo`"$obj\test\\`" /Fe`"$testExe`" " + $files

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
            & cmd.exe /c $testBatch 2>&1 | Where-Object { $_ -notmatch 'vswhere|konnte nicht gefunden|cannot find|not recognized' } |
                ForEach-Object { "  $_" }
            $testCode = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $previous }

        if ($testCode -ne 0) { throw "Test harness $($suite.Name) could not be built (exit code $testCode)." }

        & $testExe
        if ($LASTEXITCODE -ne 0) { throw "Test harness $($suite.Name) failed." }
    }

    exit 0
}

$flags = if ($Configuration -eq "Release") { "/O2 /DNDEBUG" } else { "/Od /Zi /D_DEBUG" }

# /MT rather than /MD: static runtime, no redistributable needed.
# /EHsc: a throw in the tick must not take the game down with it.
# Deliberately one continuous string rather than an array with -join: inside an
# array literal "-join" eats the following comma as part of its right operand,
# turns it into a separator array and thereby builds silent nonsense that only
# the generated batch file makes visible.
$sourceArgs = ($sources | ForEach-Object { '"' + $_ + '"' }) -join ' '

$compile = "cl.exe /nologo /std:c++20 /W4 /WX /EHsc /MT /LD $flags " +
           "/permissive- /Zc:__cplusplus " +
           "/I`"$source`" /I`"$sdkInclude`" /I`"$d3dxInclude`" " +
           "/Fo`"$obj\\`" /Fe`"$out\$asiName`" " +
           "$sourceArgs " +
           "/link /MACHINE:X86 /SUBSYSTEM:WINDOWS /LIBPATH:`"$d3dxLib`" user32.lib"

Write-Host "Building $asiName ($Configuration, x86) ..." -ForegroundColor Cyan

# Through a batch file rather than "cmd /c <long string>": the call contains
# paths with spaces, quotes and && - passing that through PowerShell to cmd
# falls apart reliably, and without any usable error message from the compiler.
# A file does not have that problem.
$batch = Join-Path $out "build.cmd"
@(
    "@echo off",
    "call `"$vcvars`" x86 >nul",
    "if errorlevel 1 exit /b 1",
    $compile,
    "exit /b %ERRORLEVEL%"
) | Set-Content -Path $batch -Encoding ASCII

# Under Windows PowerShell 5.1 "Stop" turns every stderr line of a native program
# into an aborting NativeCommandError. vcvarsall.bat warns on stderr about a
# missing vswhere.exe and still works correctly afterwards -
# without this exception the build fails over a mere warning.
$previous = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & cmd.exe /c $batch 2>&1 | ForEach-Object { "  $_" }
    $code = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previous
}

if ($code -ne 0) { throw "The compiler call failed (exit code $code)." }

$asi = Join-Path $out $asiName
if (-not (Test-Path $asi)) { throw "No $asiName produced." }

$info = Get-Item $asi
Write-Host ("Done: {0} ({1:N0} bytes)" -f $asi, $info.Length) -ForegroundColor Green

# ---------------------------------------------------------------- Signieren

& (Join-Path $PSScriptRoot "sign.ps1") -Path $asi | Out-Null
Write-Host "Signed." -ForegroundColor Green

# ------------------------------------------------------------------ Deploy

if (-not $Deploy) {
    Write-Host "With -Deploy it lands in <game>\plugins\."
    exit 0
}

$plugins = Join-Path $GamePath "plugins"
if (-not (Test-Path $plugins)) {
    throw "No plugins folder under $GamePath. First run: mliv apply ultimate-asi-loader"
}

if (Get-Process -Name GTAIV -ErrorAction SilentlyContinue) {
    throw "GTAIV.exe is running - close it first, otherwise the file is locked."
}

try {
    Copy-Item $asi (Join-Path $plugins $asiName) -Force
    Write-Host "Copied to $plugins" -ForegroundColor Green
}
catch {
    throw "Copying failed: $($_.Exception.Message)`nRun as administrator."
}

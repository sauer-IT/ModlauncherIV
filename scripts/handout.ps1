<#
.SYNOPSIS
  Puts together what gets handed to a tester.

.DESCRIPTION
  One folder, three files: the program, its checksum, and a page telling
  somebody what they are about to run.

  The note is the point. A 63 MB unsigned EXE that asks for administrator
  rights and then rewrites a game directory is, from the outside,
  indistinguishable from something you should not run - and the person opening
  it is doing you a favour. What it says is therefore what Windows is about to
  say, in advance, plus how to get out again.

  Nothing here is zipped. A zipped EXE gets flagged more often than a plain one
  by mail scanners, and the folder travels as a folder through every file
  service worth using.

.PARAMETER Out
  Where the folder goes. Defaults to artifacts\handout.

.PARAMETER Dropbox
  Also puts it in Dropbox, under one folder that keeps its name across builds -
  so a link shared once keeps working, and the people who have it get the next
  version without being sent anything.

.EXAMPLE
  .\scripts\handout.ps1
  .\scripts\handout.ps1 -Dropbox
#>
[CmdletBinding()]
param(
    [string] $Out = (Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts\handout"),
    [switch] $Dropbox
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "artifacts\release\ModlauncherIV.exe"

if (-not (Test-Path $exe)) {
    throw "No release built yet. Run .\scripts\package.ps1 first."
}

# Refuse to hand out something whose catalog the launcher would reject. The
# mistake is invisible from outside: such an EXE starts, looks perfect, and
# knows no recipe at all.
$mliv = Join-Path $root "artifacts\fd\mliv.exe"
if (Test-Path $mliv) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $mliv catalog --catalog (Join-Path $root "catalog") > $null 2>&1
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }

    if ($code -ne 0) {
        throw "The catalog does not verify against its signature. Run .\scripts\pack-trainer.ps1 and build again."
    }
}

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Path $Out -Force | Out-Null

Copy-Item $exe (Join-Path $Out "ModlauncherIV.exe")

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

"$hash *ModlauncherIV.exe" | Set-Content -Path (Join-Path $Out "SHA256.txt") -Encoding ascii

$note = @"
Modlauncher IV
==============

A guided downgrader and mod installer for GTA IV, with a trainer of its own.
One file, $size MB. Nothing to install first - double click is enough.


Before you start it: two things Windows will say
------------------------------------------------

1. "Windows protected your PC", with only a "Don't run" button visible.
   The "Run anyway" is behind "More info". This appears because the program is
   signed with a certificate nobody has heard of, not because anything is known
   to be wrong with it. If that is not good enough for you, do not run it -
   that is a reasonable answer and no offence taken.

2. It asks for administrator rights. GTA IV lives under C:\Program Files, and
   so do the backups that make undoing anything possible. Without those rights
   it can only look, not change.

You can check you got the file intact:

    Get-FileHash ModlauncherIV.exe -Algorithm SHA256

    $hash


What it does
------------

It finds the installation, tells you what state it is in, and walks through
whatever is needed - downgrading the game, the runtime it needs, the mods you
tick. Each of those downloads from its original source and is checked against a
checksum before anything is written.

Every change is backed up first, one mod at a time. The home page lists what is
installed with a Remove next to each. Nothing is a one-way door.

In the game, F8 opens the trainer menu, or both stick buttons on a controller.


If you want it gone
-------------------

Apps & features, "Modlauncher IV", Uninstall. It offers to put the game back to
how it found it on the way out.


When something goes wrong
-------------------------

There is a "Log" button at the bottom of the main page. It opens a file that
says what the program did, in the order it did it - that is the useful thing to
send, along with what the screen said. If the program will not start at all, the
file is here:

    %LOCALAPPDATA%\ModlauncherIV\launcher.log

And if something went wrong in the game rather than in the launcher, the trainer
writes its own, next to itself - or, when the game folder is not writable
without administrator rights, beside the launcher's:

    <game folder>\plugins\sauer.log
    %LOCALAPPDATA%\ModlauncherIV\sauer.log

Known and not yet fixed:

  - Only ever run on one machine, with the Rockstar Games Launcher version of
    the game. Steam and Epic installations are found by tested code, but no
    real one has ever been in front of it. If yours is not found, the wizard
    takes a folder you pick yourself - and that path works.
  - Three of the mods cannot be downloaded automatically - Nexus does not allow
    it. The program says which file to fetch and where to put it.
  - FusionFix and the three that build on it are limited to game version
    1.0.8.0. On 1.0.7.0 they crash, which is measured, not suspected.
"@

Set-Content -Path (Join-Path $Out "READ ME FIRST.txt") -Value $note -Encoding utf8

Write-Host ""
Write-Host "Ready to hand out: $Out" -ForegroundColor Green
Get-ChildItem $Out | ForEach-Object { "  {0,-24} {1,10:N0} bytes" -f $_.Name, $_.Length }
Write-Host ""
Write-Host "  sha256  $hash" -ForegroundColor DarkGray

# ------------------------------------------------------------------- Dropbox

if (-not $Dropbox) { return }

# The path out of Dropbox's own info.json rather than guessed at. Somebody who
# moved their Dropbox folder, or runs a business account alongside a personal
# one, has a path no guess would find - and writing the files into a folder
# that only looks like Dropbox syncs nothing while appearing to have worked.
$info = Join-Path $env:LOCALAPPDATA "Dropbox\info.json"
if (-not (Test-Path $info)) {
    throw "Dropbox does not appear to be installed - no $info"
}

$config = Get-Content $info -Raw | ConvertFrom-Json
$base = $config.personal.path
if (-not $base) { $base = $config.business.path }

if (-not $base -or -not (Test-Path $base)) {
    throw "Dropbox's info.json names a folder that is not there: $base"
}

# One folder, same name every time. A link shared once then keeps working, and
# whoever has it picks up the next build without being sent anything - which is
# the whole difference between handing out a file and handing out a place.
$target = Join-Path $base "Modlauncher IV"
New-Item -ItemType Directory -Path $target -Force | Out-Null

Get-ChildItem $Out -File | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $target $_.Name) -Force
}

Write-Host ""
Write-Host "Copied into Dropbox: $target" -ForegroundColor Green
Write-Host "Dropbox is uploading now - wait for the tick before sharing the link." -ForegroundColor DarkGray
Write-Host "Right-click the folder in Explorer -> Share, and send that link." -ForegroundColor DarkGray

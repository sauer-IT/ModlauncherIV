<#
.SYNOPSIS
  Builds the trainer and makes it installable through the catalog.

.DESCRIPTION
  The trainer is the only file in the catalog we produce ourselves. Its recipe
  therefore cannot be maintained by hand: checksum and size change with every
  build, and a recipe carrying yesterday's checksum rejects exactly the file it
  is supposed to install.

  So the recipe is generated, not written:

    1. build the trainer
    2. put the ASI into the shipped payload (src\Launcher.App\bundled)
    3. write catalog\mliv-trainer.json with the measured checksum
    4. sign the catalog again - the change invalidates the old index

  Without step 4 the launcher afterwards loads nothing at all, and rightly so: a
  recipe file that does not match the signed index is, from its point of view,
  indistinguishable from a tampered one.

  The trainer deliberately has no URL. Putting it somewhere so that our own
  launcher can download it again would be a detour with one more thing that can
  fail - it sits next to the program and is taken from there.

  appliesToVersions lists only 1.0.7.0, even though the launcher also knows
  1.0.8.0 and 1.0.4.0. The reason is in GameVersion.cpp: the trainer only runs
  on 1.0.7.0 and refuses to load on anything else. Were more listed here, the
  wizard would happily install it on 1.0.8.0, and the user would end up with a
  file in the plugins folder that silently does nothing.

.PARAMETER Key
  Private catalog key. Without it the build runs but nothing gets signed.

.EXAMPLE
  .\scripts\pack-trainer.ps1
  .\scripts\pack-trainer.ps1 -Key "$env:LOCALAPPDATA\ModlauncherIV\keys\catalog-signing.pem"
#>
[CmdletBinding()]
param(
    [string] $Key = (Join-Path $env:LOCALAPPDATA "ModlauncherIV\keys\catalog-signing.pem")
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------- 1. Building

Write-Host "Building the trainer ..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "build-trainer.ps1")
if ($LASTEXITCODE -ne 0) { throw "The trainer build failed." }

$asi = Join-Path $root "artifacts\trainer\ModlauncherIV-Trainer.asi"
if (-not (Test-Path $asi)) { throw "Built ASI not found: $asi" }

# ------------------------------------------------ 2. Into the shipped payload

$bundled = Join-Path $root "src\Launcher.App\bundled"
New-Item -ItemType Directory -Path $bundled -Force | Out-Null
Copy-Item $asi (Join-Path $bundled "ModlauncherIV-Trainer.asi") -Force

$hash = (Get-FileHash $asi -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $asi).Length

Write-Host "  SHA-256  $hash"
Write-Host "  Size     $size bytes"

# The recipe release carries the checksum in its name.
#
# Otherwise the reason shows up in red on the home page: the planner compares
# recipe releases, not file contents. If the release stayed the same on every
# build, a freshly built trainer would carry the same number as the installed
# one - the planner considers it done, and the user sees "file changed" without
# being offered anything that fixes it.
#
# With the checksum in the name every build produces a new release, and an
# update is offered exactly when something actually changed.

$feature = "0.4.0"
$version = "$feature+$($hash.Substring(0, 8))"

Write-Host "  Release  $version"

# ------------------------------------------------------------------ 3. Recipe

$recipe = @"
{
  "id": "mliv-trainer",
  "name": "Modlauncher IV Trainer",
  "version": "$version",
  "game": "GtaIV",
  "description": "The trainer menu of this project. In game F7 opens it; it is operated with the numpad or the arrow keys. Player, weapons, vehicles and world. Key bindings and menu position live in ModlauncherIV-Trainer.ini, which is created on the first start.",

  "appliesToVersions": [ "1.0.7.0" ],

  "requires": [ "ultimate-asi-loader" ],

  "sources": [
    {
      "id": "asi",
      "fileName": "ModlauncherIV-Trainer.asi",
      "sha256": "$hash",
      "sizeBytes": $size,
      "urls": [],
      "note": "Shipped with the launcher rather than downloaded. This file is produced by scripts\\pack-trainer.ps1; the checksum is measured at build time."
    }
  ],

  "steps": [
    { "type": "ensureDirectory", "target": "plugins" },
    { "type": "copyFile", "source": "ModlauncherIV-Trainer.asi", "target": "plugins\\ModlauncherIV-Trainer.asi" }
  ]
}
"@

$recipePath = Join-Path $root "catalog\mliv-trainer.json"
Set-Content -Path $recipePath -Value $recipe -Encoding utf8
Write-Host "Recipe written: $recipePath" -ForegroundColor Green

# ----------------------------------------------------------------- 4. Signing

if (-not (Test-Path $Key)) {
    Write-Host ""
    Write-Host "No key under $Key - the catalog stays unsigned." -ForegroundColor Yellow
    Write-Host "The launcher will then load NO recipe. To sign:" -ForegroundColor Yellow
    Write-Host "  .\scripts\pack-trainer.ps1 -Key <path\to\key.pem>" -ForegroundColor Yellow
    exit 0
}

$mliv = Join-Path $root "artifacts\fd\mliv.exe"
if (-not (Test-Path $mliv)) {
    Write-Host "Building the CLI in order to sign the catalog ..." -ForegroundColor Cyan
    & dotnet publish (Join-Path $root "src\Launcher.Cli") -c Release -o (Join-Path $root "artifacts\fd") -v q
}

# Native stderr makes Windows PowerShell abort under "Stop", even when the call
# succeeds - deliberately give way here.
$previous = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & $mliv catalog-sign --catalog (Join-Path $root "catalog") --key $Key
    $code = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previous
}

if ($code -ne 0) { throw "Signing failed (exit code $code)." }

Write-Host ""
Write-Host "Done. The trainer can now be installed through the wizard." -ForegroundColor Green

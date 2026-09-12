<#
.SYNOPSIS
  Builds the shippable release: one single clickable EXE.

.DESCRIPTION
  Self-contained, meaning the complete .NET runtime sits inside. That costs
  around 60 MB but spares the user exactly what such tools usually fail on: an
  error message about a missing runtime instead of a program. .NET 10 is new
  enough that almost nobody has it installed.

  Catalog and the shipped trainer go into the EXE as well
  (IncludeAllContentForSelfExtract). On start .NET unpacks them into a folder
  under TEMP, and AppContext.BaseDirectory points there - exactly the path
  AppPaths looks for the catalog and the shipped payload under. So it stays one
  file without the code having to know anything about packaging.

  The order matters: the trainer first, then signing the catalog, then the EXE.
  Building the EXE first packs the old catalog.

.PARAMETER Key
  Private catalog key for the signature.

.PARAMETER SkipTrainer
  Skips the trainer build. Only sensible when nothing about the trainer
  changed - otherwise the checksum in the recipe will not match the file.

.EXAMPLE
  .\scripts\package.ps1
#>
[CmdletBinding()]
param(
    [string] $Key = (Join-Path $env:LOCALAPPDATA "ModlauncherIV\keys\catalog-signing.pem"),
    [switch] $SkipTrainer
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\release"

# --------------------------------------------------- 1. Trainer and catalog

if (-not $SkipTrainer) {
    # The runtime first, the trainer second: pack-trainer signs the catalog at
    # the end, and a signature over a catalog that changes afterwards is worth
    # nothing - the launcher would then load no recipe at all.
    Write-Host "== Fetching the VC++ 2005 runtime ==" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "pack-vc80.ps1")
    if ($LASTEXITCODE -ne 0) { throw "pack-vc80 failed." }

    Write-Host "`n== Building the trainer and signing the catalog ==" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "pack-trainer.ps1") -Key $Key
    if ($LASTEXITCODE -ne 0) { throw "pack-trainer failed." }
}

$index = Join-Path $root "catalog\index.json.sig"
if (-not (Test-Path $index)) {
    throw "The catalog is not signed. Without a signature the launcher loads no recipe."
}

# That the signature exists says nothing about whether it still fits.
#
# -SkipTrainer skips the signing along with the trainer, so a recipe added or
# removed since the last run leaves an index that no longer matches the folder.
# The launcher is then right to load nothing at all - and the EXE looks perfect
# from the outside. Cheaper to ask once here than to ship it.
$mliv = Join-Path $root "artifacts\fd\mliv.exe"
if (Test-Path $mliv) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $listed = & $mliv catalog --catalog (Join-Path $root "catalog") 2>&1 | Out-String
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }

    if ($code -ne 0) {
        throw @"
The signed index does not match the catalog folder - the launcher would load no
recipe at all. Sign it again:

  .\scripts\pack-trainer.ps1

$listed
"@
    }

    $count = [regex]::Match($listed, "(\d+) recipe").Groups[1].Value
    Write-Host "Catalog: $count recipe(s), signature checks out." -ForegroundColor Green
}

# --------------------------------------------------------------- 2. Building

Write-Host "`n== Building the EXE ==" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

# Clear out obj and bin as well.
#
# The WPF build puts generated files there, and after a change to the csproj it
# no longer finds its own .baml - the error then reads "file not found" and
# points at something the build itself should have written. For a release, a
# clean start is the right thing to do anyway.

foreach ($dir in @("obj", "bin")) {
    $path = Join-Path $root "src\Launcher.App\$dir"
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

$previous = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & dotnet publish (Join-Path $root "src\Launcher.App") `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $out -v q --nologo
    $code = $LASTEXITCODE
}
finally { $ErrorActionPreference = $previous }

if ($code -ne 0) { throw "The build failed (exit code $code)." }

$exe = Join-Path $out "ModlauncherIV.exe"
if (-not (Test-Path $exe)) { throw "ModlauncherIV.exe was not produced." }

# Anything that is not the one file would contradict the point of the exercise.
# If something is left over it should be noticed rather than shipped.
$extra = Get-ChildItem $out -File | Where-Object { $_.Name -ne "ModlauncherIV.exe" }
if ($extra) {
    Write-Host "Sitting next to the EXE:" -ForegroundColor Yellow
    $extra | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Yellow }
}

# --------------------------------------------------------------- 3. Signing

& (Join-Path $PSScriptRoot "sign.ps1") -Path $exe

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "Done: $exe ($size MB)" -ForegroundColor Green
Write-Host "One file. A double click is enough - .NET does not have to be installed." -ForegroundColor Green
Write-Host ""
Write-Host "On start Windows asks for administrator rights. That is intended:" -ForegroundColor DarkGray
Write-Host "GTA IV sits under Program Files, and nobody writes there without them." -ForegroundColor DarkGray
